using MediQueue.Consultant.Config;
using MediQueue.Consultant.Services;
using MediQueue.Consultant.ViewModels;
using MediQueue.Consultant.Views;
using Microsoft.Extensions.Logging;

namespace MediQueue.Consultant;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        RemoveEntryUnderline();

        var dataDirectory = FileSystem.AppDataDirectory;   // stockage local privé de l'application

        // ── Services techniques ──
        builder.Services.AddSingleton<ISecretStore, MauiSecretStore>();
        builder.Services.AddSingleton<ISessionStore>(sp => new SessionStore(dataDirectory, sp.GetRequiredService<ISecretStore>()));
        builder.Services.AddSingleton<SessionContext>();
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();

        // ── Simulation ou API réelle : UN seul choix, dans AppConfig.Mode ──
        if (AppConfig.Mode == ServiceMode.Simulation)
        {
            builder.Services.AddSingleton(_ => new MockConsultantService(dataDirectory, AppConfig.SimulatedLatencyMs));
            builder.Services.AddSingleton<IConsultantService>(sp => sp.GetRequiredService<MockConsultantService>());
            builder.Services.AddSingleton<ISimulationControl>(sp => sp.GetRequiredService<MockConsultantService>());
        }
        else
        {
            builder.Services.AddSingleton(_ => new HttpClient
            {
                BaseAddress = new Uri(AppConfig.ResolveBaseUrl(DeviceInfo.Current.DeviceType == DeviceType.Virtual)),
                Timeout = TimeSpan.FromSeconds(AppConfig.HttpTimeoutSeconds)
            });
            builder.Services.AddSingleton<IConsultantService, ApiConsultantService>();
            builder.Services.AddSingleton<ISimulationControl, NoSimulation>();
        }

        // ── ViewModels et pages (injection de dépendances) : deux écrans seulement ──
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<LoginPage>();

        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<DashboardPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    /// <summary>Supprime le soulignement par défaut des Entry Android (la maquette utilise des champs encadrés).</summary>
    private static void RemoveEntryUnderline()
    {
#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        });
#endif
    }
}
