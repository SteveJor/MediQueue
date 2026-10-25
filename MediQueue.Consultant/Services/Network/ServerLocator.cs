using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;

namespace MediQueue.Consultant.Services.Network;

/// <summary>Prouve qu'un serveur candidat détient bien la clé attendue ET parle MediQueue, sans envoyer de donnée sensible.</summary>
public interface IEndpointVerifier
{
    Task<bool> VerifyAsync(ServerEndpoint endpoint, IReadOnlyList<string> pins, CancellationToken ct);
}

/// <summary>
/// Retrouve le serveur MediQueue sur le réseau local.
///
/// Découverte ≠ confiance : DNS-SD ne prouve rien (n'importe quel appareil peut annoncer _mediqueue._tcp). On applique donc :
///  1. filtre d'indices PUBLICS du TXT (version, TLS, empreinte « fp », centre « c ») : écarte les serveurs qui ne sont pas les nôtres ;
///  2. vérification cryptographique : poignée de main TLS dont la clé doit correspondre à un pin, puis GET /api/v1/health.
/// Plusieurs serveurs détectés : ceux dont l'indice ne correspond pas sont ignorés ; parmi les autres, le premier qui
/// prouve son identité est retenu (tous détiennent la même clé, donc la même identité). Aucun ne la prouve → erreur
/// « non fiable » : on ne se rabat JAMAIS sur un serveur non vérifié. La recherche s'arrête dès qu'un serveur est retenu
/// et au plus tard après DiscoveryTimeout : jamais de recherche permanente.
/// </summary>
public sealed class ServerLocator
{
    private readonly IServiceDiscovery _discovery;
    private readonly IEndpointVerifier _verifier;
    private readonly NetworkOptions _options;

    public ServerLocator(IServiceDiscovery discovery, IEndpointVerifier verifier, NetworkOptions options)
    {
        _discovery = discovery;
        _verifier = verifier;
        _options = options;
    }

    public async Task<ServerEndpoint> LocateAsync(
        IReadOnlyList<string> pins, string? centerHint, ServerEndpoint? cached, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.DebugEndpointOverride))
        {
            if (!ServerEndpoint.TryParse(_options.DebugEndpointOverride, out var forced))
                throw new ServerLocationException(ServerLocationError.NotFound, "Adresse de développement invalide.");
            return await _verifier.VerifyAsync(forced, pins, ct)
                ? forced
                : throw Untrusted();
        }

        // Chemin rapide : le dernier serveur connu répond-il toujours, avec la bonne clé ?
        if (cached is not null && await _verifier.VerifyAsync(cached, pins, ct)) return cached;

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(_options.DiscoveryTimeout);

        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = 0;
        try
        {
            await foreach (var service in _discovery.BrowseAsync(_options.ServiceType, window.Token))
            {
                seen++;
                if (!LooksLikeOurs(service, pins, centerHint)) continue;
                var endpoint = new ServerEndpoint(service.Host, service.Port);
                if (!tried.Add(endpoint.ToString())) continue;               // même service rapporté plusieurs fois
                if (await _verifier.VerifyAsync(endpoint, pins, window.Token)) return endpoint;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // fin de la fenêtre de recherche : on conclut avec ce qui a été vu
        }

        if (seen == 0)
            throw new ServerLocationException(ServerLocationError.NotFound,
                "Serveur MediQueue introuvable sur ce réseau. Vérifiez que vous êtes connecté au Wi-Fi du centre.");
        throw Untrusted();
    }

    private static ServerLocationException Untrusted() => new(ServerLocationError.Untrusted,
        "Un serveur a été trouvé mais son identité n'a pas pu être vérifiée. Aucune donnée n'a été envoyée.");

    /// <summary>Filtre sur les indices PUBLICS du TXT. Ce n'est pas de la sécurité : seule la vérification TLS authentifie.</summary>
    internal static bool LooksLikeOurs(DiscoveredService service, IReadOnlyList<string> pins, string? centerHint)
    {
        if (string.IsNullOrWhiteSpace(service.Host) || service.Port is < 1 or > 65535) return false;
        if (!service.Txt.TryGetValue("v", out var version) || version != "1") return false;       // version de protocole connue
        if (!service.Txt.TryGetValue("tls", out var tls) || tls != "1") return false;             // jamais de HTTP en clair
        if (service.Txt.TryGetValue("fp", out var fp) && !string.IsNullOrEmpty(fp) && !PinValidator.HintMatchesAny(pins, fp))
            return false;                                                                         // clé d'un autre serveur
        if (!string.IsNullOrWhiteSpace(centerHint) && service.Txt.TryGetValue("c", out var center)
            && !string.IsNullOrEmpty(center) && !string.Equals(center, centerHint, StringComparison.OrdinalIgnoreCase))
            return false;                                                                         // autre centre
        return true;
    }
}

/// <summary>Vérification réelle : poignée de main TLS épinglée puis GET /api/v1/health (aucune donnée sensible envoyée).</summary>
public sealed class PinnedHealthVerifier : IEndpointVerifier
{
    private readonly NetworkOptions _options;

    public PinnedHealthVerifier(NetworkOptions options) => _options = options;

    public async Task<bool> VerifyAsync(ServerEndpoint endpoint, IReadOnlyList<string> pins, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient(PinnedHandlerFactory.Create(pins, _options.VerifyTimeout))
            {
                Timeout = _options.VerifyTimeout
            };
            using var response = await http.GetAsync(new Uri(endpoint.BaseUri, "api/v1/health"), ct);
            if (!response.IsSuccessStatusCode) return false;
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("service", out var service) && service.GetString() == "mediqueue";
        }
        catch (Exception ex) when (ex is HttpRequestException or AuthenticationException or JsonException or IOException
                                       or TaskCanceledException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) throw;
            return false;     // injoignable, certificat/clé inattendus, réponse hors protocole : non vérifié = non fiable
        }
    }
}
