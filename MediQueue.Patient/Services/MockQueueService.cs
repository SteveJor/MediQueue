using MediQueue.Patient.Models;

namespace MediQueue.Patient.Services;

/// <summary>
/// SIMULATION : reproduit le comportement du backend sans réseau.
/// - État de départ identique à la maquette : N°24 en consultation, N°25-27 en attente, prochain ticket = N°28.
/// - L'état est PERSISTÉ dans un fichier JSON : il survit à la fermeture de l'application.
/// - La file n'avance JAMAIS avec le temps : seule SimulateNextCallAsync() (rôle du consultant) la fait avancer.
/// </summary>
public sealed class MockQueueService : IQueueService, ISimulationControl
{
    /// <summary>Jeton du QR de démonstration (voir docs/qr-demo).</summary>
    public const string DemoToken = "SM-MG-DEMO01";

    private const string DemoCenterName = "Centre Saint-Martin";
    private const string DemoServiceName = "Médecine générale";
    private const int MaxNameLength = 40;

    private readonly JsonFileStore<MockState> _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _latencyMs;

    public MockQueueService(string dataDirectory, int latencyMs = 400)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = new JsonFileStore<MockState>(Path.Combine(dataDirectory, "mock_queue.json"));
        _latencyMs = latencyMs;
    }

    // ───────────── IQueueService ─────────────

    public async Task<QrTarget> ValidateQrAsync(string qrToken, CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);
        EnsureKnownToken(qrToken);
        return new QrTarget
        {
            Token = DemoToken,
            CenterId = 1,
            CenterName = DemoCenterName,
            ServiceId = 1,
            ServiceName = DemoServiceName
        };
    }

    public async Task<Ticket> CreateTicketAsync(string qrToken, string? patientName, CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);
        EnsureKnownToken(qrToken);

        await _gate.WaitAsync(ct);
        try
        {
            var state = await _file.LoadAsync() ?? new MockState();
            var number = state.LastNumber + 1;
            state.LastNumber = number;
            state.Waiting.Add(number);

            var record = new MockTicketRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Number = number,
                PatientName = CleanName(patientName)
            };
            state.Tickets.Add(record);

            await _file.SaveAsync(state);
            return ToTicket(state, record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Ticket> GetTicketAsync(string ticketId, CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var state = await _file.LoadAsync() ?? new MockState();
            return ToTicket(state, FindRecord(state, ticketId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Ticket> CancelTicketAsync(string ticketId, CancellationToken ct = default)
    {
        await SimulateLatencyAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var state = await _file.LoadAsync() ?? new MockState();
            var record = FindRecord(state, ticketId);

            // Un patient déjà appelé ne peut plus quitter la file.
            if (state.Waiting.Remove(record.Number))
            {
                record.Cancelled = true;
                await _file.SaveAsync(state);
            }
            return ToTicket(state, record);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ───────────── ISimulationControl ─────────────

    public bool IsAvailable => true;

    public async Task<string> SimulateNextCallAsync()
    {
        await SimulateLatencyAsync(CancellationToken.None);
        await _gate.WaitAsync();
        try
        {
            var state = await _file.LoadAsync() ?? new MockState();
            if (state.Waiting.Count == 0)
                return "File vide : aucun patient à appeler.";

            // Même règle que le backend : le consultant termine le patient en cours,
            // puis appelle le plus ancien ticket en attente.
            var next = state.Waiting.Min();
            state.Waiting.Remove(next);
            state.InConsultation = next;
            await _file.SaveAsync(state);
            return $"Le consultant simulé a appelé le N°{next}.";
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

    private Task SimulateLatencyAsync(CancellationToken ct)
        => _latencyMs > 0 ? Task.Delay(_latencyMs, ct) : Task.CompletedTask;

    private static void EnsureKnownToken(string qrToken)
    {
        if (!string.Equals(qrToken, DemoToken, StringComparison.Ordinal))
            throw new QueueServiceException(QueueError.InvalidQr, "Ce QR code n'est pas reconnu par MediQueue.");
    }

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    private static MockTicketRecord FindRecord(MockState state, string ticketId)
        => state.Tickets.FirstOrDefault(t => t.Id == ticketId)
           ?? throw new QueueServiceException(QueueError.TicketNotFound, "Ce ticket n'existe plus.");

    private static Ticket ToTicket(MockState state, MockTicketRecord record)
    {
        TicketStatus status;
        if (record.Cancelled) status = TicketStatus.Cancelled;
        else if (record.Number == state.InConsultation) status = TicketStatus.InConsultation;
        else if (state.Waiting.Contains(record.Number)) status = TicketStatus.Waiting;
        else status = TicketStatus.Completed;

        var ahead = status == TicketStatus.Waiting
            ? state.Waiting.Where(n => n < record.Number).OrderBy(n => n).ToList()
            : new List<int>();

        return new Ticket
        {
            Id = record.Id,
            Number = record.Number,
            CenterName = DemoCenterName,
            ServiceName = DemoServiceName,
            PatientName = record.PatientName,
            Status = status,
            PeopleAhead = ahead.Count,
            Position = status == TicketStatus.Waiting ? ahead.Count + 1 : 0,
            AheadNumbers = ahead
        };
    }
}

/// <summary>Contenu du fichier mock_queue.json.</summary>
public sealed class MockState
{
    public int InConsultation { get; set; } = 24;
    public List<int> Waiting { get; set; } = new() { 25, 26, 27 };
    public int LastNumber { get; set; } = 27;
    public List<MockTicketRecord> Tickets { get; set; } = new();
}

public sealed class MockTicketRecord
{
    public string Id { get; set; } = string.Empty;
    public int Number { get; set; }
    public string? PatientName { get; set; }
    public bool Cancelled { get; set; }
}
