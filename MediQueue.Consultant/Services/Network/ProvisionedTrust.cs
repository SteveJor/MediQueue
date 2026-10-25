using System.Text.Json;

namespace MediQueue.Consultant.Services.Network;

/// <summary>
/// Empreintes de confiance du Consultant : fournies par l'administrateur du centre, hors réseau, dans le fichier
/// Resources/Raw/mediqueue_trust.json embarqué dans l'application (généré par « python -m app.cli export-trust »).
/// Ce fichier est PUBLIC et non secret : il ne contient que l'empreinte de la clé publique du serveur.
/// Sans fichier ou sans empreinte valide, l'application refuse de se connecter à quoi que ce soit.
/// </summary>
public sealed class ProvisionedTrust : ITrustAnchors
{
    private readonly Func<Task<Stream?>> _open;
    private TrustInfo? _info;

    public ProvisionedTrust(Func<Task<Stream?>> open) => _open = open;

    public string MissingTrustMessage =>
        "Application non configurée pour un centre : l'empreinte du serveur est absente. Contactez l'administrateur.";

    public async Task<TrustInfo> GetAsync(CancellationToken ct = default)
        => _info ??= await LoadAsync(ct);

    private async Task<TrustInfo> LoadAsync(CancellationToken ct)
    {
        var empty = new TrustInfo(Array.Empty<string>(), null);
        try
        {
            await using var stream = await _open();
            if (stream is null) return empty;
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return empty;

            var pins = new List<string>();
            if (root.TryGetProperty("pins", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && PinValidator.IsValidPinFormat(item.GetString())
                        && !pins.Contains(item.GetString()!))
                        pins.Add(item.GetString()!);

            var center = root.TryGetProperty("center_code", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()?.Trim().ToUpperInvariant() : null;
            return new TrustInfo(pins.Take(PinValidator.MaxPins).ToList(), string.IsNullOrEmpty(center) ? null : center);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return empty;     // fichier absent ou illisible : aucune confiance, donc aucune connexion
        }
    }
}
