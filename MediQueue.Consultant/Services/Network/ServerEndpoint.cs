namespace MediQueue.Consultant.Services.Network;

/// <summary>Un service découvert par DNS-SD (avant toute vérification d'identité).</summary>
public sealed record DiscoveredService(string Name, string Host, int Port, IReadOnlyDictionary<string, string> Txt);

/// <summary>Adresse d'un serveur MediQueue dont l'identité a été vérifiée (ou à vérifier).</summary>
public sealed record ServerEndpoint(string Host, int Port)
{
    public Uri BaseUri => new($"https://{(Host.Contains(':') ? $"[{Host}]" : Host)}:{Port}/");
    public override string ToString() => $"{Host}:{Port}";

    public static bool TryParse(string? text, out ServerEndpoint endpoint)
    {
        endpoint = new ServerEndpoint(string.Empty, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var index = text.LastIndexOf(':');
        if (index <= 0 || !int.TryParse(text[(index + 1)..], out var port) || port is < 1 or > 65535) return false;
        endpoint = new ServerEndpoint(text[..index].Trim('[', ']'), port);
        return endpoint.Host.Length > 0;
    }
}

public sealed class NetworkOptions
{
    /// <summary>Type DNS-SD tel que le demande NsdManager (point final, sans « local »).</summary>
    public string ServiceType { get; init; } = "_mediqueue._tcp.";
    public TimeSpan DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan VerifyTimeout { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Développement SEULEMENT (émulateur Android : le multicast n'atteint pas le PC). "hôte:port" ; le pin reste vérifié.</summary>
    public string? DebugEndpointOverride { get; init; }
}
