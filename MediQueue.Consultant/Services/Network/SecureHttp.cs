using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace MediQueue.Consultant.Services.Network;

/// <summary>Fabrique des gestionnaires HTTP qui n'acceptent QUE la clé épinglée (aucun repli sur « tout accepter »).</summary>
public static class PinnedHandlerFactory
{
    public static SocketsHttpHandler Create(IReadOnlyCollection<string> pins, TimeSpan connectTimeout)
    {
        var snapshot = pins.ToArray();      // copie : la liste d'origine pourrait changer pendant la vie du gestionnaire
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,      // une redirection pourrait envoyer la requête vers un autre hôte
            UseProxy = false,               // réseau local : pas de proxy système
            ConnectTimeout = connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,   // pas de CRL sur un réseau local : le pin fait foi
                RemoteCertificateValidationCallback = (_, certificate, _, _) => PinValidator.IsTrusted(certificate, snapshot)
            }
        };
    }
}

/// <summary>Source des empreintes de confiance (pins) : QR code côté Patient, fichier provisionné côté Consultant.</summary>
public interface ITrustAnchors
{
    Task<TrustInfo> GetAsync(CancellationToken ct = default);
    /// <summary>Message affiché quand aucune empreinte n'est disponible.</summary>
    string MissingTrustMessage { get; }
}

public sealed record TrustInfo(IReadOnlyList<string> Pins, string? CenterHint);

public interface ISecureHttpClientProvider
{
    /// <summary>Client HTTPS dont la clé du serveur est vérifiée ; (re)découvre le serveur si nécessaire.</summary>
    Task<HttpClient> GetClientAsync(string? centerHint, CancellationToken ct);

    /// <summary>Oublie le serveur courant : le prochain appel relance la découverte (changement de réseau, serveur redémarré…).</summary>
    void Invalidate();
}

public sealed class SecureHttpClientProvider : ISecureHttpClientProvider, IDisposable
{
    private readonly ITrustAnchors _trust;
    private readonly ServerLocator _locator;
    private readonly NetworkOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpClient? _client;
    private ServerEndpoint? _endpoint;
    private string _key = string.Empty;
    private volatile bool _stale;        // posé par Invalidate() sans jamais bloquer (appelé depuis le fil d'interface)

    public SecureHttpClientProvider(ITrustAnchors trust, ServerLocator locator, NetworkOptions options)
    {
        _trust = trust;
        _locator = locator;
        _options = options;
    }

    public async Task<HttpClient> GetClientAsync(string? centerHint, CancellationToken ct)
    {
        var trust = await _trust.GetAsync(ct);
        if (trust.Pins.Count == 0)
            throw new ServerLocationException(ServerLocationError.NoTrustAnchor, _trust.MissingTrustMessage);

        var key = string.Join('|', trust.Pins) + "#" + (centerHint ?? trust.CenterHint ?? string.Empty);
        await _gate.WaitAsync(ct);
        try
        {
            if (_client is not null && key == _key && !_stale) return _client;

            _client?.Dispose();
            _client = null;
            var endpoint = await _locator.LocateAsync(trust.Pins, centerHint ?? trust.CenterHint, _endpoint, ct);
            _client = new HttpClient(PinnedHandlerFactory.Create(trust.Pins, _options.VerifyTimeout))
            {
                BaseAddress = endpoint.BaseUri,
                Timeout = _options.HttpTimeout
            };
            _endpoint = endpoint;
            _key = key;
            _stale = false;
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Marque le client comme périmé ; le prochain appel relance la découverte. Ne bloque jamais
    /// (une recherche peut être en cours et tenir le verrou plusieurs secondes). _endpoint reste le « dernier serveur connu ».</summary>
    public void Invalidate() => _stale = true;

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }
}
