using System.Diagnostics;
using Microsoft.Maui.Controls;
using MediQueue.Patient.Services;

namespace MediQueue.Patient.ViewModels;

/// <summary>
/// Écran de scan : permission caméra, démarrage/arrêt de la caméra, validation du QR par le service.
/// La page (code-behind) se contente de transmettre le texte lu à QrDetectedCommand.
/// </summary>
public sealed class ScanViewModel : ViewModelBase
{
    public const string DefaultFooter = "Le QR code est reconnu dès qu'il est visible.";

    private readonly IQueueService _queue;
    private readonly INavigationService _navigation;
    private readonly ICameraPermissionService _permissions;

    private bool _isCameraActive;
    private bool _isScanPaused;
    private bool _hasCameraPermission = true;
    private bool _hasError;
    private string _footerMessage = DefaultFooter;
    private bool _isProcessing;

    public ScanViewModel(IQueueService queue, INavigationService navigation, ICameraPermissionService permissions)
    {
        _queue = queue;
        _navigation = navigation;
        _permissions = permissions;

        GoBackCommand = new Command(async () => await _navigation.GoBackAsync());
        QrDetectedCommand = new Command<string>(async raw => await OnQrDetectedAsync(raw));
        RetryPermissionCommand = new Command(async () => await CheckPermissionAndStartAsync());
        OpenSettingsCommand = new Command(_permissions.OpenAppSettings);
    }

    public Command GoBackCommand { get; }
    public Command<string> QrDetectedCommand { get; }
    public Command RetryPermissionCommand { get; }
    public Command OpenSettingsCommand { get; }

    /// <summary>Durée pendant laquelle un message d'erreur reste affiché avant de relancer le scan.</summary>
    public int ErrorPauseMs { get; set; } = 2200;

    public bool IsCameraActive { get => _isCameraActive; set => SetProperty(ref _isCameraActive, value); }
    public bool IsScanPaused { get => _isScanPaused; set => SetProperty(ref _isScanPaused, value); }

    public bool HasCameraPermission
    {
        get => _hasCameraPermission;
        private set
        {
            if (SetProperty(ref _hasCameraPermission, value))
                OnPropertyChanged(nameof(IsPermissionDenied));
        }
    }

    public bool IsPermissionDenied => !_hasCameraPermission;
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string FooterMessage { get => _footerMessage; private set => SetProperty(ref _footerMessage, value); }

    // ───────────── Cycle de vie ─────────────

    public async Task OnAppearingAsync()
    {
        _isProcessing = false;
        HasError = false;
        FooterMessage = DefaultFooter;
        await CheckPermissionAndStartAsync();
    }

    public void OnDisappearing()
    {
        IsCameraActive = false;   // la caméra est toujours coupée en quittant la page
    }

    private async Task CheckPermissionAndStartAsync()
    {
        var granted = await _permissions.EnsureGrantedAsync();
        HasCameraPermission = granted;
        if (!granted) return;

        IsScanPaused = false;
        IsCameraActive = true;
    }

    // ───────────── Lecture d'un QR code ─────────────

    private async Task OnQrDetectedAsync(string? raw)
    {
        // La caméra détecte plusieurs fois par seconde : on ne traite qu'une lecture à la fois.
        if (_isProcessing || string.IsNullOrWhiteSpace(raw)) return;
        _isProcessing = true;
        IsScanPaused = true;

        try
        {
            if (!QrPayloadParser.TryExtractToken(raw, out var token))
            {
                await ShowErrorThenResumeAsync("Ce QR code n'est pas un QR code MediQueue.");
                return;
            }

            FooterMessage = "Vérification du QR code…";
            var target = await _queue.ValidateQrAsync(token);

            // Succès : on laisse _isProcessing à true ; il sera remis à zéro au retour sur la page.
            await _navigation.GoToAsync(Routes.TicketName, new Dictionary<string, object> { ["target"] = target });
        }
        catch (QueueServiceException ex)
        {
            await ShowErrorThenResumeAsync(ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Scan] Erreur inattendue : {ex}");
            await ShowErrorThenResumeAsync("Une erreur inattendue est survenue. Réessayez.");
        }
    }

    private async Task ShowErrorThenResumeAsync(string message)
    {
        HasError = true;
        FooterMessage = message;

        if (ErrorPauseMs > 0) await Task.Delay(ErrorPauseMs);

        HasError = false;
        FooterMessage = DefaultFooter;
        IsScanPaused = false;
        _isProcessing = false;
    }
}
