using MediQueue.Patient.Services.Network;

namespace MediQueue.Patient.Services.Network;

/// <summary>
/// Empreintes de confiance du Patient. Elles viennent UNIQUEMENT du QR code scanné dans le centre :
///  - « candidates » tant que le patient n'a pas obtenu de ticket (un QR abandonné ou falsifié ne remplace rien) ;
///  - « enregistrées » (trust.json) une fois le ticket créé : elles servent ensuite à suivre ce ticket, sans nouveau scan.
/// </summary>
public interface ICandidateTrust
{
    void SetCandidate(IReadOnlyList<string> pins);
    void ClearCandidate();
    Task CommitAsync();
}

public sealed class StoredTrust
{
    public List<string> Pins { get; set; } = new();
}

public sealed class PatientTrustStore : ITrustAnchors, ICandidateTrust
{
    private readonly JsonFileStore<StoredTrust> _file;
    private readonly object _lock = new();
    private StoredTrust? _stored;
    private List<string>? _candidate;

    public PatientTrustStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = new JsonFileStore<StoredTrust>(Path.Combine(dataDirectory, "trust.json"));
    }

    public string MissingTrustMessage => "Ce QR code ne contient pas l'empreinte de sécurité du serveur. Demandez à l'accueil du centre un QR code à jour.";

    public async Task<TrustInfo> GetAsync(CancellationToken ct = default)
    {
        _stored ??= await _file.LoadAsync() ?? new StoredTrust();
        lock (_lock)
        {
            var pins = _candidate ?? _stored.Pins.Where(PinValidator.IsValidPinFormat).ToList();
            return new TrustInfo(pins, null);
        }
    }

    public void SetCandidate(IReadOnlyList<string> pins)
    {
        lock (_lock)
        {
            var valid = pins.Where(PinValidator.IsValidPinFormat).Distinct().Take(PinValidator.MaxPins).ToList();
            _candidate = valid.Count == 0 ? null : valid;
        }
    }

    public void ClearCandidate()
    {
        lock (_lock) _candidate = null;
    }

    public async Task CommitAsync()
    {
        List<string>? toSave;
        lock (_lock)
        {
            toSave = _candidate;
            _candidate = null;
        }
        if (toSave is null) return;
        _stored = new StoredTrust { Pins = toSave };
        await _file.SaveAsync(_stored);
    }
}
