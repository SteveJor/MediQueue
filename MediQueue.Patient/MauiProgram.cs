using BarcodeScanning;
using MediQueue.Patient.Config;
using MediQueue.Patient.Services;
using MediQueue.Patient.ViewModels;
using MediQueue.Patient.Views;
using Microsoft.Extensions.Logging;

namespace MediQueue.Patient;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeScanning();   // scanner QR natif (ML Kit sur Android)

        RemoveEntryUnderline();

        var dataDirectory = FileSystem.AppDataDirectory;   // stockage local privé de l'application

        // ── Services techniques ──
        builder.Services.AddSingleton<ITicketStore>(_ => new JsonTicketStore(dataDirectory));
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<IDialogService, ShellDialogService>();
        builder.Services.AddSingleton<ICameraPermissionService, CameraPermissionService>();

        // ── Simulation ou API réelle : UN seul choix, dans AppConfig.Mode ──
        if (AppConfig.Mode == ServiceMode.Simulation)
        {
            builder.Services.AddSingleton(_ => new MockQueueService(dataDirectory, AppConfig.SimulatedLatencyMs));
            builder.Services.AddSingleton<IQueueService>(sp => sp.GetRequiredService<MockQueueService>());
            builder.Services.AddSingleton<ISimulationControl>(sp => sp.GetRequiredService<MockQueueService>());
        }
        else
        {
            builder.Services.AddSingleton(_ => new HttpClient
            {
                BaseAddress = new Uri(AppConfig.ResolveBaseUrl(DeviceInfo.Current.DeviceType == DeviceType.Virtual)),
                Timeout = TimeSpan.FromSeconds(AppConfig.HttpTimeoutSeconds)
            });
            builder.Services.AddSingleton<IQueueService, ApiQueueService>();
            builder.Services.AddSingleton<ISimulationControl, NoSimulation>();
        }

        // ── ViewModels et pages (injection de dépendances) ──
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<HomePage>();

        builder.Services.AddTransient<ScanViewModel>();
        builder.Services.AddTransient<ScanPage>();

        builder.Services.AddTransient<TicketNameViewModel>();
        builder.Services.AddTransient<TicketNamePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    /// <summary>Supprime le soulignement par défaut des Entry Android (la maquette utilise un champ encadré).</summary>
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
