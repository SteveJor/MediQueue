using Microsoft.Maui.Controls;

namespace MediQueue.Patient.Services;

/// <summary>Message modal simple (la logique reste dans le ViewModel, l'affichage ici).</summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, string okText = "OK");
}

public sealed class ShellDialogService : IDialogService
{
    public async Task ShowMessageAsync(string title, string message, string okText = "OK")
    {
        var page = Shell.Current?.CurrentPage;
        if (page is null) return;
#pragma warning disable CS0618 // DisplayAlert est remplacé par DisplayAlertAsync en .NET 10 (avertissement seulement)
        await page.DisplayAlert(title, message, okText);
#pragma warning restore CS0618
    }
}
