namespace MediQueue.Consultant.Models;

/// <summary>Session du consultant après authentification (renvoyée par le backend).</summary>
public sealed class Session
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string CenterCode { get; set; } = string.Empty;
    public string ConsultantCode { get; set; } = string.Empty;
    public string CenterName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;

    public bool IsExpired(DateTime? nowUtc = null) => (nowUtc ?? DateTime.UtcNow) >= ExpiresAtUtc;
}

/// <summary>Derniers identifiants saisis (jamais le mot de passe), pour pré-remplir l'écran de connexion.</summary>
public sealed class LoginHints
{
    public string CenterCode { get; set; } = string.Empty;
    public string ConsultantCode { get; set; } = string.Empty;
}
