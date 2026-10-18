using System.Diagnostics;
using Microsoft.Maui.Controls;
using MediQueue.Patient.Models;
using MediQueue.Patient.Services;

namespace MediQueue.Patient.ViewModels;

/// <summary>
/// « Votre ticket est prêt » : confirmation du centre/service, nom FACULTATIF, création du ticket.
/// Reçoit le QrTarget par la navigation Shell ([QueryProperty], comme dans le TP).
/// </summary>
[QueryProperty(nameof(Target), "target")]
public sealed class TicketNameViewModel : ViewModelBase
{
    private readonly IQueueService _queue;
    private readonly ITicketStore _store;
    private readonly INavigationService _navigation;

    private QrTarget? _target;
    private string _patientName = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;

    public TicketNameViewModel(IQueueService queue, ITicketStore store, INavigationService navigation)
    {
        _queue = queue;
        _store = store;
        _navigation = navigation;

        ContinueCommand = new Command(async () => await ContinueAsync(), () => !IsBusy && _target is not null);
    }

    public Command ContinueCommand { get; }

    public QrTarget? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value)) return;
            _target = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CenterName));
            OnPropertyChanged(nameof(ServiceName));
            ContinueCommand.ChangeCanExecute();
        }
    }

    public string CenterName => _target?.CenterName ?? string.Empty;
    public string ServiceName => _target?.ServiceName ?? string.Empty;

    /// <summary>Champ « Votre nom » (Mode=TwoWay par défaut sur Entry.Text).</summary>
    public string PatientName { get => _patientName; set => SetProperty(ref _patientName, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                ContinueCommand.ChangeCanExecute();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    private async Task ContinueAsync()
    {
        if (_target is null || IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var name = string.IsNullOrWhiteSpace(PatientName) ? null : PatientName.Trim();
            var ticket = await _queue.CreateTicketAsync(_target.Token, name);

            await _store.SaveAsync(ticket);          // persistance locale du ticket actif
            await _navigation.GoToAsync(Routes.Home); // retour automatique à l'accueil
        }
        catch (QueueServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TicketName] Erreur inattendue : {ex}");
            ErrorMessage = "Une erreur inattendue est survenue. Réessayez.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
