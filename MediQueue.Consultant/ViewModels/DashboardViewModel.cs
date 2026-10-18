using System.Diagnostics;
using Microsoft.Maui.Controls;
using MediQueue.Consultant.Config;
using MediQueue.Consultant.Models;
using MediQueue.Consultant.Services;

namespace MediQueue.Consultant.ViewModels;

/// <summary>
/// Écran 2 : tableau de bord. Le consultant est le SEUL acteur qui fait avancer la file :
/// bouton → bottom sheet de confirmation → requête au serveur → tableau de bord remis à jour + notification.
/// Aucun mécanisme automatique n'appelle de patient (l'actualisation périodique est en lecture seule).
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly IConsultantService _service;
    private readonly SessionContext _context;
    private readonly ISessionStore _store;
    private readonly INavigationService _navigation;
    private readonly ISimulationControl _simulation;

    private Dashboard? _dashboard;
    private IReadOnlyList<WaitingRow> _rows = Array.Empty<WaitingRow>();
    private bool _isLoaded;
    private bool _isBusy;
    private bool _isRefreshing;
    private bool _isSheetOpen;
    private string _sheetTitle = string.Empty;
    private string _sheetMessage = string.Empty;
    private int _pendingNumber;
    private string? _connectionMessage;
    private string _snackbarMessage = string.Empty;
    private bool _isSnackbarVisible;
    private string _simulationMessage = string.Empty;
    private int _stateVersion;           // incrémenté à chaque état appliqué : évite d'écraser un état récent par une lecture ancienne
    private int _refreshGate;            // 0 = libre, 1 = lecture en cours
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _snackbarCts;

    public DashboardViewModel(IConsultantService service, SessionContext context, ISessionStore store,
        INavigationService navigation, ISimulationControl simulation)
    {
        _service = service;
        _context = context;
        _store = store;
        _navigation = navigation;
        _simulation = simulation;

        CallNextCommand = new Command(OpenCallSheet, () => CanCallNext);
        ConfirmCallCommand = new Command(async () => await ConfirmCallAsync());
        DismissSheetCommand = new Command(CloseSheet);
        RefreshCommand = new Command(async () => await RefreshAsync());
        SimulateNewPatientCommand = new Command(async () => await SimulateNewPatientAsync());
        ResetSimulationCommand = new Command(async () => await ResetSimulationAsync());
    }

    // ───────────── Commandes ─────────────

    public Command CallNextCommand { get; }
    public Command ConfirmCallCommand { get; }
    public Command DismissSheetCommand { get; }
    public Command RefreshCommand { get; }
    public Command SimulateNewPatientCommand { get; }
    public Command ResetSimulationCommand { get; }

    /// <summary>Durée d'affichage de la notification (réglable pour les tests).</summary>
    public int SnackbarDurationMs { get; set; } = AppConfig.SnackbarSeconds * 1000;

    // ───────────── État d'affichage ─────────────

    public string CenterName => _dashboard?.CenterName ?? _context.Current?.CenterName ?? string.Empty;
    public string ServiceName => _dashboard?.ServiceName ?? _context.Current?.ServiceName ?? string.Empty;

    public bool HasCurrent => _dashboard?.CurrentNumber is not null;
    public string CurrentNumberText => _dashboard?.CurrentNumber is { } n ? $"N°{n}" : "—";
    public string CurrentCaption => HasCurrent ? "En consultation" : "Aucun patient en consultation";

    public IReadOnlyList<WaitingRow> WaitingRows => _rows;
    public bool HasWaiting => _rows.Count > 0;
    public bool IsQueueEmpty => !HasWaiting;
    public string WaitingCountText => _rows.Count <= 1 ? $"{_rows.Count} patient" : $"{_rows.Count} patients";
    public string EmptyQueueText => _isLoaded ? "Aucun patient en attente." : "Chargement de la file…";

    public int NextNumber => _dashboard?.WaitingNumbers.FirstOrDefault() ?? 0;

    /// <summary>Bouton « Appeler le patient suivant » : désactivé sans patient en attente ou pendant un appel.</summary>
    public bool CanCallNext => HasWaiting && !_isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanCallNext));
                CallNextCommand.ChangeCanExecute();
            }
        }
    }

    public bool IsRefreshing { get => _isRefreshing; set => SetProperty(ref _isRefreshing, value); }

    public string? ConnectionMessage
    {
        get => _connectionMessage;
        private set
        {
            if (SetProperty(ref _connectionMessage, value))
                OnPropertyChanged(nameof(HasConnectionMessage));
        }
    }

    public bool HasConnectionMessage => !string.IsNullOrEmpty(_connectionMessage);

    // Bottom sheet de confirmation
    public bool IsSheetOpen { get => _isSheetOpen; private set => SetProperty(ref _isSheetOpen, value); }
    public string SheetTitle { get => _sheetTitle; private set => SetProperty(ref _sheetTitle, value); }
    public string SheetMessage { get => _sheetMessage; private set => SetProperty(ref _sheetMessage, value); }

    // Notification temporaire (« Patient N°25 appelé »)
    public string SnackbarMessage { get => _snackbarMessage; private set => SetProperty(ref _snackbarMessage, value); }
    public bool IsSnackbarVisible { get => _isSnackbarVisible; private set => SetProperty(ref _isSnackbarVisible, value); }

    // Outils de simulation (visibles uniquement en mode simulation)
    public bool IsSimulationVisible => _simulation.IsAvailable;
    public string SimulationMessage { get => _simulationMessage; private set => SetProperty(ref _simulationMessage, value); }

    // ───────────── Cycle de vie de la page ─────────────

    public async Task OnAppearingAsync()
    {
        if (!_context.IsAuthenticated)
        {
            await ExpireSessionAsync("Session expirée. Reconnectez-vous.");
            return;
        }

        RaiseHeaderChanged();
        await RefreshAsync();
        StartAutoRefresh();
    }

    public void OnDisappearing() => StopAutoRefresh();

    // ───────────── Lecture du tableau de bord (jamais de modification de la file) ─────────────

    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshGate, 1) == 1)
        {
            IsRefreshing = false;
            return;
        }

        try
        {
            var version = _stateVersion;
            var fresh = await _service.GetDashboardAsync();

            // Un appel confirmé (ou une autre lecture) a appliqué un état plus récent entre-temps : on ignore cette lecture.
            if (version != _stateVersion) return;

            Apply(fresh);
            ConnectionMessage = null;
        }
        catch (ConsultantServiceException ex) when (ex.Error == ConsultantError.Unauthorized)
        {
            await ExpireSessionAsync(ex.Message);
        }
        catch (ConsultantServiceException)
        {
            ConnectionMessage = "Connexion indisponible. Les informations affichées peuvent ne plus être à jour.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Dashboard] Erreur inattendue pendant l'actualisation : {ex}");
        }
        finally
        {
            IsRefreshing = false;
            Interlocked.Exchange(ref _refreshGate, 0);
        }
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh();
        if (AppConfig.AutoRefreshSeconds <= 0) return;

        var cts = new CancellationTokenSource();
        _autoRefreshCts = cts;
        _ = AutoRefreshLoopAsync(cts.Token);
    }

    private void StopAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
    }

    private async Task AutoRefreshLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(AppConfig.AutoRefreshSeconds));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (!_isBusy) await RefreshAsync();   // jamais pendant un appel en cours
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt normal quand la page disparaît
        }
    }

    // ───────────── Appel du patient suivant ─────────────

    private void OpenCallSheet()
    {
        if (!CanCallNext) return;

        _pendingNumber = NextNumber;   // on fige le numéro que le consultant voit et va confirmer
        SheetTitle = $"Appeler le patient N°{_pendingNumber} ?";
        SheetMessage = $"Le N°{_pendingNumber} passera en consultation et sera retiré de la file d'attente.";
        IsSheetOpen = true;
    }

    private void CloseSheet() => IsSheetOpen = false;   // Annuler : aucune modification

    private async Task ConfirmCallAsync()
    {
        var expected = _pendingNumber;
        CloseSheet();
        if (expected <= 0 || IsBusy) return;
        _pendingNumber = 0;             // une confirmation = au plus une requête

        IsBusy = true;
        try
        {
            var result = await _service.CallNextAsync(expected);
            Apply(result.Dashboard);
            ConnectionMessage = null;
            ShowSnackbar($"Patient N°{result.CalledNumber} appelé");
        }
        catch (ConsultantServiceException ex) when (ex.Error == ConsultantError.Unauthorized)
        {
            await ExpireSessionAsync(ex.Message);
        }
        catch (ConsultantServiceException ex) when (ex.Error is ConsultantError.QueueChanged or ConsultantError.QueueEmpty)
        {
            ShowSnackbar(ex.Message);
            await RefreshAsync();       // on réaffiche la vraie file
        }
        catch (ConsultantServiceException ex)
        {
            ShowSnackbar(ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Dashboard] Erreur inattendue pendant l'appel : {ex}");
            ShowSnackbar("Une erreur inattendue est survenue.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowSnackbar(string message)
    {
        _snackbarCts?.Cancel();
        _snackbarCts?.Dispose();
        var cts = new CancellationTokenSource();
        _snackbarCts = cts;

        SnackbarMessage = message;
        IsSnackbarVisible = true;
        _ = HideSnackbarLaterAsync(cts.Token);
    }

    private async Task HideSnackbarLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SnackbarDurationMs, token);
            IsSnackbarVisible = false;
        }
        catch (OperationCanceledException)
        {
            // une notification plus récente a pris le relais
        }
    }

    private async Task ExpireSessionAsync(string message)
    {
        StopAutoRefresh();
        _context.Clear();
        await _store.ClearSessionAsync();
        await _navigation.GoToAsync(Routes.Login, new Dictionary<string, object> { ["notice"] = message });
    }

    // ───────────── Simulation ─────────────

    private async Task SimulateNewPatientAsync()
    {
        if (!_simulation.IsAvailable) return;
        try
        {
            SimulationMessage = await _simulation.SimulateNewPatientAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Dashboard] Simulation : {ex}");
            SimulationMessage = "La simulation a échoué.";
        }
    }

    private async Task ResetSimulationAsync()
    {
        if (!_simulation.IsAvailable) return;
        try
        {
            await _simulation.ResetAsync();
            await RefreshAsync();
            SimulationMessage = "Simulation réinitialisée (N°24 en consultation, N°25 à 28 en attente).";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Dashboard] Réinitialisation : {ex}");
            SimulationMessage = "La réinitialisation a échoué.";
        }
    }

    // ───────────── Application d'un état à l'écran ─────────────

    private void Apply(Dashboard dashboard)
    {
        _stateVersion++;
        _dashboard = dashboard;
        _isLoaded = true;
        _rows = dashboard.WaitingNumbers
            .Select((number, index) => new WaitingRow($"N°{number}", index == 0 ? "Prochain patient" : "En attente"))
            .ToList();

        RaiseHeaderChanged();
        OnPropertyChanged(nameof(HasCurrent));
        OnPropertyChanged(nameof(CurrentNumberText));
        OnPropertyChanged(nameof(CurrentCaption));
        OnPropertyChanged(nameof(WaitingRows));
        OnPropertyChanged(nameof(HasWaiting));
        OnPropertyChanged(nameof(IsQueueEmpty));
        OnPropertyChanged(nameof(WaitingCountText));
        OnPropertyChanged(nameof(EmptyQueueText));
        OnPropertyChanged(nameof(NextNumber));
        OnPropertyChanged(nameof(CanCallNext));
        CallNextCommand.ChangeCanExecute();
    }

    private void RaiseHeaderChanged()
    {
        OnPropertyChanged(nameof(CenterName));
        OnPropertyChanged(nameof(ServiceName));
    }
}
