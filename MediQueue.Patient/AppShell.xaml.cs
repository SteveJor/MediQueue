using MediQueue.Patient.Services;
using MediQueue.Patient.Views;

namespace MediQueue.Patient;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pages « empilées » : enregistrées par route (comme A3 dans le TP).
        Routing.RegisterRoute(Routes.Scan, typeof(ScanPage));
        Routing.RegisterRoute(Routes.TicketName, typeof(TicketNamePage));
    }
}
