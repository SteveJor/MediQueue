using MediQueue.Patient.Models;

namespace MediQueue.Patient.Services;

/// <summary>
/// CONTRAT unique entre l'app Patient et la file d'attente.
/// Deux implémentations : MockQueueService (simulation) et ApiQueueService (FastAPI).
/// Toutes lèvent QueueServiceException avec un message déjà lisible par l'utilisateur.
/// </summary>
public interface IQueueService
{
    /// <summary>Vérifie un jeton QR et renvoie le centre + service. Ne crée aucun ticket.</summary>
    Task<QrTarget> ValidateQrAsync(string qrToken, CancellationToken ct = default);

    /// <summary>Crée le ticket (nom facultatif).</summary>
    Task<Ticket> CreateTicketAsync(string qrToken, string? patientName, CancellationToken ct = default);

    /// <summary>Relit le ticket et sa position (lecture seule).</summary>
    Task<Ticket> GetTicketAsync(string ticketId, CancellationToken ct = default);

    /// <summary>Le patient quitte la file (action volontaire, bouton « Annuler » de la maquette).</summary>
    Task<Ticket> CancelTicketAsync(string ticketId, CancellationToken ct = default);
}

/// <summary>
/// Outils de test du mode simulation (le consultant n'existe pas dans l'app Patient).
/// En mode API : IsAvailable = false et rien n'est affiché.
/// </summary>
public interface ISimulationControl
{
    bool IsAvailable { get; }

    /// <summary>Joue l'action du consultant « Appeler le patient suivant » ; renvoie un message.</summary>
    Task<string> SimulateNextCallAsync();

    /// <summary>Remet la file fictive à l'état de la maquette (N°24 en consultation, 25-27 en attente).</summary>
    Task ResetAsync();
}

public sealed class NoSimulation : ISimulationControl
{
    public bool IsAvailable => false;
    public Task<string> SimulateNextCallAsync() => Task.FromResult(string.Empty);
    public Task ResetAsync() => Task.CompletedTask;
}
