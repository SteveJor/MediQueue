using System.Diagnostics;
using Microsoft.Maui.Controls;
using MediQueue.Patient.Config;
using MediQueue.Patient.Models;
using MediQueue.Patient.Services;

namespace MediQueue.Patient.ViewModels;

/// <summary>
/// Accueil : deux états dans la même page (aucun ticket / ticket actif).
/// Le ViewModel ne connaît ni la page, ni les contrôles : il expose des propriétés et des commandes.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private enum SheetKind { None, JoinQueue, CancelTicket }

    private readonly IQueueService _queue;
    private readonly ITicketStore _store;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ISimulationControl _simulation;

    private Ticket? _ticket;
    private IReadOnlyList<QueueRow> _queueRows = Array.Empty<QueueRow>();
    private bool _isRefreshing;
    private bool _isSheetOpen;
    private string _sheetTitle = string.Empty;
    private string _sheetMessage = string.Empty;
    private string _sheetCancelText = string.Empty;
    private string _sheetConfirmText = string.Empty;
    private SheetKind _sheetKind = SheetKind.None;
    private string? _connectionMessage;
    private string _simulationMessage = string.Empty;
    private CancellationTokenSource? _autoRefreshCts;
    private int _refreshGate;       // 0 = libre, 1 = actualisation en cours
    private bool _busy;

    public HomeViewModel(IQueueService queue, ITicketStore store, INavigationService navigation,
        IDialogService dialogs, ISimulationControl simulation)
    {
        _queue = queue;
        _store = store;
        _navigation = navigation;
        _dialogs = dialogs;
        _simulation = simulation;

        ScanCommand = new Command(OpenJoinSheet);
        CancelTicketCommand = new Command(OpenCancelSheet, () => CanCancelTicket);
        ConfirmSheetCommand = new Command(async () => await ConfirmSheetAsync());
        DismissSheetCommand = new Command(CloseSheet);
        RefreshCommand = new Command(async () => await RefreshAsync());
        SimulateCallNextCommand = new Command(async () => await SimulateCallNextAsync());
        ResetSimulationCommand = new Command(async () => await ResetSimulationAsync());
    }

    // ───────────── Commandes ─────────────

    public Command ScanCommand { get; }
    public Command CancelTicketCommand { get; }
    public Command ConfirmSheetCommand { get; }
    public Command DismissSheetCommand { get; }
    public Command RefreshCommand { get; }
    public Command SimulateCallNextCommand { get; }
    public Command ResetSimulationCommand { get; }

    // ───────────── État d'affichage ─────────────

    public bool HasTicket => _ticket is not null;
    public bool HasNoTicket => _ticket is null;

    public string TicketNumberText => _ticket is null ? string.Empty : $"N°{_ticket.Number}";
    public string CenterName => _ticket?.CenterName ?? string.Empty;
    public string ServiceName => _ticket?.ServiceName ?? string.Empty;

    public string StatusText => _ticket?.Status switch
    {
        TicketStatus.Waiting => "En attente",
        TicketStatus.InConsultation => "En consultation",
        TicketStatus.Completed => "Terminé",
        TicketStatus.Cancelled => "Annulé",
        _ => string.Empty
    };

    public string HeaderTitle => _ticket?.Status == TicketStatus.InConsultation
        ? "C'est votre tour."
        : "Vous êtes dans la file.";

    public string HeaderSubtitle => _ticket?.Status == TicketStatus.InConsultation
        ? "Présentez-vous au service."
        : "Installez-vous, nous vous appellerons.";

    /// <summary>« Ma file d'attente » n'a de sens que tant que le patient attend.</summary>
    public bool ShowQueueSection => _ticket?.Status == TicketStatus.Waiting;

    public string AheadSummary => _ticket is { Status: TicketStatus.Waiting } t
        ? t.PeopleAhead switch
        {
            0 => "Vous êtes le prochain",
            1 => "1 avant vous",
            var n => $"{n} avant vous"
        }
        : string.Empty;

    public IReadOnlyList<QueueRow> QueueRows => _queueRows;

    public bool CanCancelTicket => AppConfig.EnableTicketCancellation && _ticket?.Status == TicketStatus.Waiting;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

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

    // Bottom sheet (une seule instance, contenu variable)
    public bool IsSheetOpen { get => _isSheetOpen; private set => SetProperty(ref _isSheetOpen, value); }
    public string SheetTitle { get => _sheetTitle; private set => SetProperty(ref _sheetTitle, value); }
    public string SheetMessage { get => _sheetMessage; private set => SetProperty(ref _sheetMessage, value); }
    public string SheetCancelText { get => _sheetCancelText; private set => SetProperty(ref _sheetCancelText, value); }
    public string SheetConfirmText { get => _sheetConfirmText; private set => SetProperty(ref _sheetConfirmText, value); }

    // Outils de simulation (visibles uniquement en mode simulation)
    public bool IsSimulationVisible => _simulation.IsAvailable;
    public string SimulationMessage { get => _simulationMessage; private set => SetProperty(ref _simulationMessage, value); }

    // ───────────── Cycle de vie de la page ─────────────

    public async Task OnAppearingAsync()
    {
        // 1) Affichage immédiat depuis le stockage local (fonctionne hors connexion).
        ApplyTicket(await _store.LoadAsync());

        // 2) Relecture auprès du serveur, puis relecture périodique (lecture seule).
        await RefreshAsync();
        StartAutoRefresh();
    }

    public void OnDisappearing() => StopAutoRefresh();

    // ───────────── Actualisation (jamais de modification de la file) ─────────────

    private async Task RefreshAsync()
    {
        var current = _ticket;
        if (current is null)
        {
            ConnectionMessage = null;
            IsRefreshing = false;
            return;
        }

        if (Interlocked.Exchange(ref _refreshGate, 1) == 1)
        {
            IsRefreshing = false;
            return;
        }

        try
        {
            var fresh = await _queue.GetTicketAsync(current.Id);
            await ApplyServerTicketAsync(fresh);
            ConnectionMessage = null;
        }
        catch (QueueServiceException ex) when (ex.Error == QueueError.TicketNotFound)
        {
            // Le serveur ne connaît plus ce ticket : on libère l'accueil.
            await _store.ClearAsync();
            ApplyTicket(null);
            ConnectionMessage = null;
        }
        catch (QueueServiceException)
        {
            // Hors connexion : on garde le dernier état connu et on le signale discrètement.
            ConnectionMessage = "Connexion indisponible. Dernière position connue affichée.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Erreur inattendue pendant l'actualisation : {ex}");
        }
        finally
        {
            IsRefreshing = false;
            Interlocked.Exchange(ref _refreshGate, 0);
        }
    }

    private async Task ApplyServerTicketAsync(Ticket fresh)
    {
        if (fresh.Status is TicketStatus.Completed or TicketStatus.Cancelled)
        {
            await _store.ClearAsync();
            ApplyTicket(null);
            return;
        }

        await _store.SaveAsync(fresh);
        ApplyTicket(fresh);
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
                await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // arrêt normal quand la page disparaît
        }
    }

    // ───────────── Bottom sheet ─────────────

    private void OpenJoinSheet()
    {
        _sheetKind = SheetKind.JoinQueue;
        SheetTitle = "Rejoindre une file";
        SheetMessage = "Scannez le QR code affiché dans votre centre pour obtenir votre ticket.";
        SheetCancelText = "Annuler";
        SheetConfirmText = "Scanner";
        IsSheetOpen = true;
    }

    private void OpenCancelSheet()
    {
        _sheetKind = SheetKind.CancelTicket;
        SheetTitle = "Annuler mon ticket ?";
        SheetMessage = "Vous quitterez la file d'attente et perdrez votre position.";
        SheetCancelText = "Garder";
        SheetConfirmText = "Annuler le ticket";
        IsSheetOpen = true;
    }

    private void CloseSheet()
    {
        IsSheetOpen = false;
        _sheetKind = SheetKind.None;
    }

    private async Task ConfirmSheetAsync()
    {
        var kind = _sheetKind;
        CloseSheet();

        switch (kind)
        {
            case SheetKind.JoinQueue:
                await _navigation.GoToAsync(Routes.Scan);
                break;
            case SheetKind.CancelTicket:
                await CancelTicketAsync();
                break;
        }
    }

    private async Task CancelTicketAsync()
    {
        var current = _ticket;
        if (current is null || _busy) return;

        _busy = true;
        try
        {
            await _queue.CancelTicketAsync(current.Id);
            await _store.ClearAsync();
            ApplyTicket(null);
        }
        catch (QueueServiceException ex) when (ex.Error == QueueError.TicketNotFound)
        {
            await _store.ClearAsync();
            ApplyTicket(null);
        }
        catch (QueueServiceException ex)
        {
            await _dialogs.ShowMessageAsync("Annulation impossible", ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Erreur inattendue pendant l'annulation : {ex}");
            await _dialogs.ShowMessageAsync("Annulation impossible", "Une erreur inattendue est survenue.");
        }
        finally
        {
            _busy = false;
        }
    }

    // ───────────── Simulation ─────────────

    private async Task SimulateCallNextAsync()
    {
        if (!_simulation.IsAvailable) return;
        try
        {
            SimulationMessage = await _simulation.SimulateNextCallAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Simulation : {ex}");
            SimulationMessage = "La simulation a échoué.";
        }
    }

    private async Task ResetSimulationAsync()
    {
        if (!_simulation.IsAvailable) return;
        try
        {
            await _simulation.ResetAsync();
            await _store.ClearAsync();
            ApplyTicket(null);
            SimulationMessage = "Simulation réinitialisée (N°24 en consultation, N°25 à 27 en attente).";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Home] Réinitialisation : {ex}");
            SimulationMessage = "La réinitialisation a échoué.";
        }
    }

    // ───────────── Application d'un ticket à l'écran ─────────────

    private void ApplyTicket(Ticket? ticket)
    {
        _ticket = ticket;
        _queueRows = BuildRows(ticket);

        OnPropertyChanged(nameof(HasTicket));
        OnPropertyChanged(nameof(HasNoTicket));
        OnPropertyChanged(nameof(TicketNumberText));
        OnPropertyChanged(nameof(CenterName));
        OnPropertyChanged(nameof(ServiceName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(ShowQueueSection));
        OnPropertyChanged(nameof(AheadSummary));
        OnPropertyChanged(nameof(QueueRows));
        OnPropertyChanged(nameof(CanCancelTicket));
        CancelTicketCommand.ChangeCanExecute();
    }

    private static IReadOnlyList<QueueRow> BuildRows(Ticket? ticket)
    {
        if (ticket is null || ticket.Status != TicketStatus.Waiting)
            return Array.Empty<QueueRow>();

        var rows = ticket.AheadNumbers
            .OrderBy(n => n)
            .Select(n => new QueueRow($"N°{n}", "Avant vous", false))
            .ToList();

        var position = ticket.Position > 0 ? ticket.Position : rows.Count + 1;
        rows.Add(new QueueRow($"N°{ticket.Number}", $"Vous · position {position}", true));
        return rows;
    }
}
