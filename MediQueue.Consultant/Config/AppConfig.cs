namespace MediQueue.Consultant.Config;

public enum ServiceMode
{
    /// <summary>Données fictives locales (aucun serveur nécessaire).</summary>
    Simulation,

    /// <summary>Communication réelle avec l'API FastAPI.</summary>
    Api
}

/// <summary>
/// CONFIGURATION CENTRALE — seul fichier à modifier pour passer de la simulation à l'API réelle.
/// </summary>
public static class AppConfig
{
    // ════════════════ À MODIFIER ════════════════

    /// <summary>Simulation tant que le backend n'existe pas ; Api ensuite.</summary>
    public static ServiceMode Mode => ServiceMode.Simulation;

    /// <summary>Adresse IPv4 du PC qui héberge FastAPI (téléphone PHYSIQUE, même Wi-Fi).</summary>
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
    /// Relecture périodique du tableau de bord (lecture seule : voir les nouveaux patients).
    /// Elle ne fait JAMAIS avancer la file. 0 = désactivée (tirer pour actualiser).
    /// </summary>
    public static int AutoRefreshSeconds => 5;

    /// <summary>Durée d'affichage de la notification « Patient N°25 appelé ».</summary>
    public static int SnackbarSeconds => 3;

    /// <summary>Conserve la session (jeton chiffré) pour ne pas retaper le mot de passe à chaque ouverture.</summary>
    public static bool RememberSession => true;

    public static string ResolveBaseUrl(bool isEmulator)
        => $"http://{(isEmulator ? EmulatorHost : LanHost)}:{ApiPort}";
}
