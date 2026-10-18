namespace MediQueue.Patient.Models;

/// <summary>Une ligne de « Ma file d'attente » (donnée d'affichage, sans nom de patient).</summary>
public sealed record QueueRow(string Title, string Caption, bool IsMe);
