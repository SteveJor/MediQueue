namespace MediQueue.Consultant;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // Les deux écrans sont déclarés dans AppShell.xaml (routes « login » et « dashboard »),
        // il n'y a donc aucune route supplémentaire à enregistrer.
    }
}
