namespace MediQueue.Patient.Config;

public enum ServiceMode
{
    /// <summary>Données fictives locales (aucun serveur nécessaire).</summary>
    Simulation,

    /// <summary>Communication réelle avec l'API FastAPI.</summary>
    Api
}

/// <summary>
/// CONFIGURATION CENTRALE — c'est le seul fichier à modifier pour basculer
/// de la simulation vers l'API réelle.
/// </summary>
public static class AppConfig
{
    // ════════════════ À MODIFIER ════════════════

    /// <summary>Simulation tant que le backend n'existe pas ; Api ensuite.</summary>
    public static ServiceMode Mode => ServiceMode.Simulation;

    /// <summary>
    /// Adresse IP locale du PC qui héberge FastAPI (téléphone PHYSIQUE).
    /// Windows : ipconfig → « Adresse IPv4 ». Téléphone et PC sur le même Wi-Fi.
    /// </summary>
    public static string LanHost => "192.168.1.100";

    /// <summary>Port d'écoute de FastAPI (uvicorn).</summary>
    public static int ApiPort => 8000;

    // ═════════════════════════════════════════════

    /// <summary>Sur l'émulateur Android, 10.0.2.2 désigne le PC hôte.</summary>
    public const string EmulatorHost = "10.0.2.2";

    public const int HttpTimeoutSeconds = 10;

    /// <summary>Latence artificielle du mode simulation (ms).</summary>
    public const int SimulatedLatencyMs = 400;

    /// <summary>
    /// Relecture périodique du ticket depuis le serveur (lecture seule, la file
    /// n'avance JAMAIS à cause du temps). 0 = désactivé (tirer pour actualiser).
    /// </summary>
    public static int AutoRefreshSeconds => 8;

    /// <summary>Affiche le bouton « Annuler » présent dans la maquette (voir docs/DECISIONS.md).</summary>
    public const bool EnableTicketCancellation = true;

    /// <summary>Format des QR codes : mediqueue://queue/{jeton}</summary>
    public const string QrScheme = "mediqueue";
    public const string QrHost = "queue";

    public static string ResolveBaseUrl(bool isEmulator)
        => $"http://{(isEmulator ? EmulatorHost : LanHost)}:{ApiPort}";
}
