using MediQueue.Consultant.Models;

namespace MediQueue.Consultant.Services;

/// <summary>
/// SIMULATION : reproduit le backend sans réseau.
/// - Identifiants de DÉMO (uniquement ici, jamais dans le vrai service) : SM-001 / MG-01 / demo1234.
/// - État de départ = maquette : N°24 en consultation, N°25 à 28 en attente.
/// - État PERSISTÉ dans un fichier JSON (survit à la fermeture de l'app).
/// - La file n'avance JAMAIS avec le temps : seule CallNextAsync() (confirmée par le consultant) la fait avancer.
/// </summary>
public sealed class MockConsultantService : IConsultantService, ISimulationControl
{
    public const string DemoCenterCode = "SM-001";
    public const string DemoConsultantCode = "MG-01";
    public const string DemoPassword = "demo1234";

    private const string DemoCenterName = "Centre Saint-Martin";
    private const string DemoServiceName = "Médecine générale";

    private readonly JsonFileStore<MockQueueState> _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _latencyMs;

    public MockConsultantService(string dataDirectory, int latencyMs = 400)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = new JsonFileStore<MockQueueState>(Path.Combine(dataDirectory, "mock_queue.json"));
        _latencyMs = latencyMs;
    }

    // ───────────── IConsultantService ─────────────

    public async Task<Session> LoginAsync(string centerCode, string consultantCode, string password, CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);

        var ok = string.Equals(centerCode?.Trim(), DemoCenterCode, StringComparison.OrdinalIgnoreCase)
              && string.Equals(consultantCode?.Trim(), DemoConsultantCode, StringComparison.OrdinalIgnoreCase)
              && string.Equals(password, DemoPassword, StringComparison.Ordinal);

        if (!ok)
            throw new ConsultantServiceException(ConsultantError.InvalidCredentials, "Identifiants incorrects. Vérifiez le centre, le consultant et le mot de passe.");

        return new Session
        {
            AccessToken = "mock." + Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(8),
            CenterCode = DemoCenterCode,
            ConsultantCode = DemoConsultantCode,
            CenterName = DemoCenterName,
            ServiceName = DemoServiceName
        };
    }

    public async Task<Dashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            return ToDashboard(await LoadAsync());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CallResult> CallNextAsync(int expectedNumber, CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);
        await _gate.WaitAsync(ct);   // même rôle que la transaction atomique du backend
        try
        {
            var state = await LoadAsync();

            if (state.Waiting.Count == 0)
                throw new ConsultantServiceException(ConsultantError.QueueEmpty, "Aucun patient en attente.");

            var next = state.Waiting.Min();
            if (next != expectedNumber)
                throw new ConsultantServiceException(ConsultantError.QueueChanged,
                    "La file a changé : vérifiez le prochain patient avant d'appeler.");

            // Le patient en consultation est terminé, le suivant prend sa place : une seule progression.
            state.Waiting.Remove(next);
            state.InConsultation = next;
            await _file.SaveAsync(state);

            return new CallResult { CalledNumber = next, Dashboard = ToDashboard(state) };
        }
        finally
        {
            _gate.Release();
        }
    }

    // ───────────── ISimulationControl ─────────────

    public bool IsAvailable => true;

    public async Task<string> SimulateNewPatientAsync()
    {
        await SimulateLatencyAsync(CancellationToken.None);
        await _gate.WaitAsync();
        try
        {
            var state = await LoadAsync();
            state.LastNumber++;
            state.Waiting.Add(state.LastNumber);
            await _file.SaveAsync(state);
            return $"Un patient a pris le ticket N°{state.LastNumber}.";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await _file.DeleteAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ───────────── Internes ─────────────

    private async Task<MockQueueState> LoadAsync() => await _file.LoadAsync() ?? new MockQueueState();

    private Task SimulateLatencyAsync(CancellationToken ct)
        => _latencyMs > 0 ? Task.Delay(_latencyMs, ct) : Task.CompletedTask;

    private static Dashboard ToDashboard(MockQueueState state) => new()
    {
        CenterName = DemoCenterName,
        ServiceName = DemoServiceName,
        CurrentNumber = state.InConsultation,
        WaitingNumbers = state.Waiting.OrderBy(n => n).ToList()
    };
}

/// <summary>Contenu du fichier mock_queue.json (état de départ = maquette).</summary>
public sealed class MockQueueState
{
    public int InConsultation { get; set; } = 24;
    public List<int> Waiting { get; set; } = new() { 25, 26, 27, 28 };
    public int LastNumber { get; set; } = 28;
}
