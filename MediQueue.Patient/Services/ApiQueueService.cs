using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediQueue.Patient.Models;

namespace MediQueue.Patient.Services;

/// <summary>
/// Implémentation RÉELLE : appelle l'API FastAPI (voir docs/CONTRAT-API.md).
/// Active quand AppConfig.Mode = ServiceMode.Api.
/// </summary>
public sealed class ApiQueueService : IQueueService
{
    private const string Prefix = "api/v1/patient";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    private readonly HttpClient _http;

    public ApiQueueService(HttpClient http) => _http = http;

    public async Task<QrTarget> ValidateQrAsync(string qrToken, CancellationToken ct = default)
    {
        var dto = await SendAsync<QrTargetDto>(HttpMethod.Post, $"{Prefix}/qr/validate",
            new { QrToken = qrToken }, QueueError.InvalidQr, ct);
        return new QrTarget
        {
            Token = dto.QrToken,
            CenterId = dto.CenterId,
            CenterName = dto.CenterName,
            ServiceId = dto.ServiceId,
            ServiceName = dto.ServiceName
        };
    }

    public async Task<Ticket> CreateTicketAsync(string qrToken, string? patientName, CancellationToken ct = default)
    {
        var dto = await SendAsync<TicketDto>(HttpMethod.Post, $"{Prefix}/tickets",
            new { QrToken = qrToken, PatientName = patientName }, QueueError.InvalidQr, ct);
        return dto.ToModel();
    }

    public async Task<Ticket> GetTicketAsync(string ticketId, CancellationToken ct = default)
    {
        var dto = await SendAsync<TicketDto>(HttpMethod.Get, $"{Prefix}/tickets/{Uri.EscapeDataString(ticketId)}",
            null, QueueError.TicketNotFound, ct);
        return dto.ToModel();
    }

    public async Task<Ticket> CancelTicketAsync(string ticketId, CancellationToken ct = default)
    {
        var dto = await SendAsync<TicketDto>(HttpMethod.Post, $"{Prefix}/tickets/{Uri.EscapeDataString(ticketId)}/cancel",
            null, QueueError.TicketNotFound, ct);
        return dto.ToModel();
    }

    // ───────────── Plomberie HTTP ─────────────

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body,
        QueueError notFoundError, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);

            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return result ?? throw new QueueServiceException(QueueError.Server, "Réponse vide du serveur.");
            }

            var detail = await TryReadDetailAsync(response, ct);
            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new QueueServiceException(notFoundError,
                    notFoundError == QueueError.InvalidQr
                        ? "Ce QR code n'est pas reconnu par MediQueue."
                        : "Ce ticket n'existe plus."),
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new QueueServiceException(
                    notFoundError == QueueError.InvalidQr ? QueueError.InvalidQr : QueueError.Server,
                    detail ?? "Requête refusée par le serveur."),
                _ => new QueueServiceException(QueueError.Server,
                    detail ?? "Le serveur a rencontré une erreur. Réessayez dans un instant.")
            };
        }
        catch (QueueServiceException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new QueueServiceException(QueueError.Network,
                "Impossible de joindre le serveur. Vérifiez votre connexion Wi-Fi.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new QueueServiceException(QueueError.Network,
                "Le serveur met trop de temps à répondre. Réessayez.", ex);
        }
        catch (JsonException ex)
        {
            throw new QueueServiceException(QueueError.Server, "Réponse inattendue du serveur.", ex);
        }
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

internal sealed record QrTargetDto(string QrToken, int CenterId, string CenterName, int ServiceId, string ServiceName);

internal sealed record TicketDto(
    string Id,
    int Number,
    string CenterName,
    string ServiceName,
    string? PatientName,
    TicketStatus Status,
    int PeopleAhead,
    int Position,
    List<int>? AheadNumbers)
{
    public Ticket ToModel() => new()
    {
        Id = Id,
        Number = Number,
        CenterName = CenterName,
        ServiceName = ServiceName,
        PatientName = PatientName,
        Status = Status,
        PeopleAhead = PeopleAhead,
        Position = Position,
        AheadNumbers = AheadNumbers ?? new List<int>()
    };
}
