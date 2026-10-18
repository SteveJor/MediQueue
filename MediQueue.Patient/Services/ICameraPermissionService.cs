using Microsoft.Maui.ApplicationModel;

namespace MediQueue.Patient.Services;

public interface ICameraPermissionService
{
    /// <summary>Vérifie, et demande si besoin, l'accès caméra. True si accordé.</summary>
    Task<bool> EnsureGrantedAsync();

    /// <summary>Ouvre les réglages Android de l'application (permission refusée définitivement).</summary>
    void OpenAppSettings();
}

public sealed class CameraPermissionService : ICameraPermissionService
{
    public async Task<bool> EnsureGrantedAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted) return true;

        status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Camera>());
        return status == PermissionStatus.Granted;
    }

    public void OpenAppSettings() => AppInfo.Current.ShowSettingsUI();
}
