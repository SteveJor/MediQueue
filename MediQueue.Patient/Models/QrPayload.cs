namespace MediQueue.Patient.Models;

/// <summary>Contenu d'un QR MediQueue : jeton du service + empreinte(s) du serveur du centre (canal physique de confiance).</summary>
public sealed record QrPayload(string Token, IReadOnlyList<string> Pins);
