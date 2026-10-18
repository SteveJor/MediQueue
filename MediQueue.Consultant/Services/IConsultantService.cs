using MediQueue.Consultant.Models;

namespace MediQueue.Consultant.Services;

/// <summary>
/// CONTRAT unique entre l'app Consultant et la file d'attente.
/// Deux implémentations : MockConsultantService (simulation) et ApiConsultantService (FastAPI).
/// Toutes lèvent ConsultantServiceException avec un message lisible par l'utilisateur.
/// </summary>
public interface IConsultantService
{
    /// <summary>Authentifie le consultant. Le centre et le service sont renvoyés par le serveur.</summary>
    Task<Session> LoginAsync(string centerCode, string consultantCode, string password, CancellationToken ct = default);

    /// <summary>Lit la file du service du consultant connecté (lecture seule).</summary>
    Task<Dashboard> GetDashboardAsync(CancellationToken ct = default);

    /// <summary>
    /// Appelle le patient suivant. <paramref name="expectedNumber"/> est le numéro que le consultant
    /// a vu et confirmé : si la file a changé entre-temps, le serveur refuse (QueueChanged)
    /// au lieu d'appeler un autre patient. Garantit une seule progression par confirmation.
    /// </summary>
    Task<CallResult> CallNextAsync(int expectedNumber, CancellationToken ct = default);
}

/// <summary>
/// Outils de test du mode simulation (les patients n'existent pas dans l'app Consultant).
/// En mode API : IsAvailable = false et rien n'est affiché.
/// </summary>
public interface ISimulationControl
{
    bool IsAvailable { get; }

    /// <summary>Joue l'arrivée d'un patient (comme un scan réussi côté Patient) ; renvoie un message.</summary>
    Task<string> SimulateNewPatientAsync();

    /// <summary>Remet la file fictive à l'état de la maquette (N°24 en consultation, 25 à 28 en attente).</summary>
    Task ResetAsync();
}

public sealed class NoSimulation : ISimulationControl
{
    public bool IsAvailable => false;
    public Task<string> SimulateNewPatientAsync() => Task.FromResult(string.Empty);
    public Task ResetAsync() => Task.CompletedTask;
}
