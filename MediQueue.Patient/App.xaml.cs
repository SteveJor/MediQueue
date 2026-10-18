namespace MediQueue.Patient;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // La maquette est claire : on évite que le mode sombre du téléphone casse les couleurs.
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new AppShell());
}
