namespace MediQueue.Patient.Models;

/// <summary>Centre + service identifiés (et validés) à partir d'un QR code.</summary>
public sealed class QrTarget
{
    public string Token { get; set; } = string.Empty;
    public int CenterId { get; set; }
    public string CenterName { get; set; } = string.Empty;
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
}
