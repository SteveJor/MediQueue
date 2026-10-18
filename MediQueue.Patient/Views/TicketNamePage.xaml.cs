using MediQueue.Patient.ViewModels;

namespace MediQueue.Patient.Views;

public partial class TicketNamePage : ContentPage
{
    public TicketNamePage(TicketNameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
