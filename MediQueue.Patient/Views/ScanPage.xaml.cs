using BarcodeScanning;
using MediQueue.Patient.ViewModels;

namespace MediQueue.Patient.Views;

/// <summary>
/// Code-behind volontairement minimal : cycle de vie + transmission du texte lu au ViewModel.
/// Toute la logique (parsing, validation, navigation, erreurs) est dans ScanViewModel.
/// </summary>
public partial class ScanPage : ContentPage
{
    private readonly ScanViewModel _viewModel;

    public ScanPage(ScanViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }

    // Adaptation technique imposée par le composant caméra (événement natif → commande).
    private void CameraView_OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        var raw = e.BarcodeResults
            .Select(result => result.RawValue)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (raw is not null && _viewModel.QrDetectedCommand.CanExecute(raw))
            _viewModel.QrDetectedCommand.Execute(raw);
    }
}
