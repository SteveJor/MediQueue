namespace MediQueue.Patient.Models;

/// <summary>Ticket tel que renvoyé par le service (et conservé localement).</summary>
public sealed class Ticket
{
    /// <summary>Identifiant opaque, difficile à deviner (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    public int Number { get; set; }
    public string CenterName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? PatientName { get; set; }
    public TicketStatus Status { get; set; }

    /// <summary>Nombre de patients devant celui-ci (0 si déjà appelé).</summary>
    public int PeopleAhead { get; set; }

    /// <summary>Position dans la file (1 = prochain). 0 si le ticket n'est plus en attente.</summary>
    public int Position { get; set; }

    /// <summary>Numéros (jamais les noms) des tickets devant celui-ci.</summary>
    public List<int> AheadNumbers { get; set; } = new();
}
