namespace MediQueue.Consultant.Services.Network;

public enum ServerLocationError
{
    NoTrustAnchor,        // aucune empreinte de confiance connue : on ne se connecte à rien
    NotFound,             // aucun service MediQueue découvert (hors Wi-Fi du centre, multicast filtré, serveur arrêté…)
    Untrusted,            // des serveurs ont répondu mais aucun n'a prouvé son identité : rien n'a été envoyé
    DiscoveryUnavailable  // plateforme sans découverte de services
}

public sealed class ServerLocationException : Exception
{
    public ServerLocationError Kind { get; }
    public ServerLocationException(ServerLocationError kind, string message) : base(message) => Kind = kind;
}

/// <summary>
/// Découverte de services DNS-SD (mDNS). Implémentation Android : NsdManager (Platforms/Android).
/// Cette interface isole la partie native ; la découverte ne contient AUCUNE règle métier.
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>Énumère les services résolus (hôte, port, TXT) jusqu'à annulation. Arrêter l'énumération arrête la recherche.</summary>
    IAsyncEnumerable<DiscoveredService> BrowseAsync(string serviceType, CancellationToken ct);
}

public sealed class NullServiceDiscovery : IServiceDiscovery
{
    public async IAsyncEnumerable<DiscoveredService> BrowseAsync(
        string serviceType, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        throw new ServerLocationException(ServerLocationError.DiscoveryUnavailable,
            "La recherche automatique du serveur n'est pas disponible sur cet appareil.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
