using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Consultant.Models;

namespace MediQueue.Consultant.Services;

/// <summary>
/// Implémentation RÉELLE : appelle l'API FastAPI (voir docs/CONTRAT-API.md).
/// Active quand AppConfig.Mode = ServiceMode.Api. Aucun identifiant n'est codé en dur ici.
/// </summary>
public sealed class ApiConsultantService : IConsultantService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SessionContext _context;

    public ApiConsultantService(HttpClient http, SessionContext context)
    {
        _http = http;
        _context = context;
    }

    public async Task<Session> LoginAsync(string centerCode, string consultantCode, string password, CancellationToken ct = default)
    {
        var dto = await SendAsync<LoginResponseDto>(HttpMethod.Post, "api/v1/auth/login",
            new { CenterCode = centerCode, ConsultantCode = consultantCode, Password = password },
            authenticated: false, ct);

        return new Session
        {
            AccessToken = dto.AccessToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(dto.ExpiresIn),
            CenterCode = centerCode,
            ConsultantCode = consultantCode,
            CenterName = dto.CenterName,
            ServiceName = dto.ServiceName
        };
    }

    public async Task<Dashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        var dto = await SendAsync<DashboardDto>(HttpMethod.Get, "api/v1/consultant/dashboard", null, authenticated: true, ct);
        return dto.ToModel();
    }

    public async Task<CallResult> CallNextAsync(int expectedNumber, CancellationToken ct = default)
    {
        var dto = await SendAsync<CallNextResponseDto>(HttpMethod.Post, "api/v1/consultant/queue/call-next",
            new { ExpectedNumber = expectedNumber }, authenticated: true, ct);
        return new CallResult { CalledNumber = dto.CalledNumber, Dashboard = dto.Dashboard.ToModel() };
    }

    // ───────────── Plomberie HTTP ─────────────

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, bool authenticated, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);

            if (authenticated)
            {
                var token = _context.Current?.AccessToken;
                if (string.IsNullOrEmpty(token))
                    throw new ConsultantServiceException(ConsultantError.Unauthorized, "Session expirée. Reconnectez-vous.");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return result ?? throw new ConsultantServiceException(ConsultantError.Server, "Réponse vide du serveur.");
            }

            var detail = await TryReadDetailAsync(response, ct);
            throw MapError(response.StatusCode, authenticated, detail);
        }
        catch (ConsultantServiceException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new ConsultantServiceException(ConsultantError.Network,
                "Impossible de joindre le serveur. Vérifiez la connexion Wi-Fi.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConsultantServiceException(ConsultantError.Network,
                "Le serveur met trop de temps à répondre. Réessayez.", ex);
        }
        catch (JsonException ex)
        {
            throw new ConsultantServiceException(ConsultantError.Server, "Réponse inattendue du serveur.", ex);
        }
    }

    private static ConsultantServiceException MapError(HttpStatusCode status, bool authenticated, string? detail)
    {
        // 401 sur la connexion = mauvais identifiants ; 401 ailleurs = session expirée.
        if (status == HttpStatusCode.Unauthorized)
            return authenticated
                ? new(ConsultantError.Unauthorized, "Session expirée. Reconnectez-vous.")
                : new(ConsultantError.InvalidCredentials, detail ?? "Identifiants incorrects.");

        return status switch
        {
            HttpStatusCode.Forbidden when !authenticated
                => new(ConsultantError.InvalidCredentials, detail ?? "Accès refusé pour ce compte."),
            HttpStatusCode.Forbidden
                => new(ConsultantError.Unauthorized, "Accès refusé. Reconnectez-vous."),
            HttpStatusCode.NotFound when authenticated
                => new(ConsultantError.QueueEmpty, detail ?? "Aucun patient en attente."),
            HttpStatusCode.Conflict
                => new(ConsultantError.QueueChanged, detail ?? "La file a changé : vérifiez le prochain patient avant d'appeler."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when !authenticated
                => new(ConsultantError.InvalidCredentials, "Renseignez le centre, le consultant et le mot de passe."),
            _ => new(ConsultantError.Server, detail ?? "Le serveur a rencontré une erreur. Réessayez dans un instant.")
        };
    }

    /// <summary>FastAPI renvoie {"detail": "..."} pour les erreurs applicatives.</summary>
    private static async Task<string?> TryReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // corps non JSON : on ignore
        }
        return null;
    }
}

// ───────────── DTO (noms JSON en snake_case, voir docs/CONTRAT-API.md) ─────────────

internal sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresIn, string CenterName, string ServiceName);

internal sealed record DashboardDto(string CenterName, string ServiceName, int? CurrentNumber, List<int>? WaitingNumbers)
{
    public Dashboard ToModel() => new()
    {
        CenterName = CenterName,
        ServiceName = ServiceName,
        CurrentNumber = CurrentNumber,
        WaitingNumbers = WaitingNumbers ?? new List<int>()
    };
}

internal sealed record CallNextResponseDto(int CalledNumber, DashboardDto Dashboard);
