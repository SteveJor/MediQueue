using System.Diagnostics;
using Microsoft.Maui.Controls;
using MediQueue.Consultant.Config;
using MediQueue.Consultant.Models;
using MediQueue.Consultant.Services;

namespace MediQueue.Consultant.ViewModels;

/// <summary>
/// Écran 1 : connexion (identifiant du centre, identifiant du consultant, mot de passe).
/// Le centre et le service sont renvoyés par le serveur après authentification.
/// </summary>
[QueryProperty(nameof(Notice), "notice")]
public sealed class LoginViewModel : ViewModelBase
{
    private readonly IConsultantService _service;
    private readonly SessionContext _context;
    private readonly ISessionStore _store;
    private readonly INavigationService _navigation;

    private string _centerCode = string.Empty;
    private string _consultantCode = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private bool _isCheckingSession = true;

    public LoginViewModel(IConsultantService service, SessionContext context, ISessionStore store, INavigationService navigation)
    {
        _service = service;
        _context = context;
        _store = store;
        _navigation = navigation;

        LoginCommand = new Command(async () => await LoginAsync(), () => !IsBusy);
    }

    public Command LoginCommand { get; }

    public string CenterCode { get => _centerCode; set => SetProperty(ref _centerCode, value); }
    public string ConsultantCode { get => _consultantCode; set => SetProperty(ref _consultantCode, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                LoginCommand.ChangeCanExecute();
        }
    }

    /// <summary>Tant que la session enregistrée n'a pas été examinée, seul le logo est affiché (pas de flash du formulaire).</summary>
    public bool IsCheckingSession
    {
        get => _isCheckingSession;
        private set
        {
            if (SetProperty(ref _isCheckingSession, value))
                OnPropertyChanged(nameof(IsFormVisible));
        }
    }

    public bool IsFormVisible => !_isCheckingSession;

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

    /// <summary>Message transmis par la navigation (ex. « Session expirée ») ; affiché comme l'erreur.</summary>
    public string? Notice
    {
        get => _errorMessage;
        set => ErrorMessage = value;
    }

    // ───────────── Cycle de vie ─────────────

    public async Task OnAppearingAsync()
    {
        var leaving = false;   // vrai si on part directement vers le tableau de bord (le formulaire ne doit pas clignoter)
        try
        {
            var hints = await _store.LoadHintsAsync();
            if (hints is not null)
            {
                if (string.IsNullOrEmpty(CenterCode)) CenterCode = hints.CenterCode;
                if (string.IsNullOrEmpty(ConsultantCode)) ConsultantCode = hints.ConsultantCode;
            }

            // Session encore valide (jeton chiffré) : on va directement au tableau de bord.
            if (AppConfig.RememberSession && !_context.IsAuthenticated)
            {
                var saved = await _store.LoadSessionAsync();
                if (saved is { } s && !s.IsExpired())
                {
                    _context.Set(s);
                    leaving = true;
                    await _navigation.GoToAsync(Routes.Dashboard);
                    return;
                }
            }
            else if (_context.IsAuthenticated)
            {
                leaving = true;
                await _navigation.GoToAsync(Routes.Dashboard);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Login] Lecture de la session impossible : {ex}");
        }
        finally
        {
            if (!leaving) IsCheckingSession = false;
        }
    }

    public void OnDisappearing()
    {
        Password = string.Empty;       // le mot de passe ne reste jamais en mémoire dans le champ
        if (!IsBusy) ErrorMessage = null;
    }

    // ───────────── Connexion ─────────────

    private async Task LoginAsync()
    {
        if (IsBusy) return;

        var center = CenterCode.Trim();
        var consultant = ConsultantCode.Trim();
        if (center.Length == 0 || consultant.Length == 0 || Password.Length == 0)
        {
            ErrorMessage = "Renseignez l'identifiant du centre, celui du consultant et le mot de passe.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var session = await _service.LoginAsync(center, consultant, Password);
            _context.Set(session);

            try
            {
                await _store.SaveHintsAsync(new LoginHints { CenterCode = center, ConsultantCode = consultant });
                if (AppConfig.RememberSession) await _store.SaveSessionAsync(session);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Login] Sauvegarde locale impossible (la connexion reste valide) : {ex}");
            }

            Password = string.Empty;
            await _navigation.GoToAsync(Routes.Dashboard);
        }
        catch (ConsultantServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Login] Erreur inattendue : {ex}");
            ErrorMessage = "Une erreur inattendue est survenue. Réessayez.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
