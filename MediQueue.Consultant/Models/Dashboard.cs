namespace MediQueue.Consultant.Models;

/// <summary>Photographie de la file du service du consultant (numéros uniquement).</summary>
public sealed class Dashboard
{
    public string CenterName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Numéro du patient actuellement en consultation, ou null.</summary>
    public int? CurrentNumber { get; set; }

    /// <summary>Numéros en attente, dans l'ordre de passage (le premier est le prochain).</summary>
    public List<int> WaitingNumbers { get; set; } = new();
}

/// <summary>Résultat d'un appel du patient suivant : qui a été appelé + la file mise à jour.</summary>
public sealed class CallResult
{
    public int CalledNumber { get; set; }
    public Dashboard Dashboard { get; set; } = new();
}
