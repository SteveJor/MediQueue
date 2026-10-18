using System.Windows.Input;
using ViewExtensions = Microsoft.Maui.Controls.ViewExtensions;

namespace MediQueue.Consultant.Views.Controls;

/// <summary>
/// Contrôle d'affichage pur : IsOpen ouvre/ferme le panneau avec une animation.
/// Les textes et commandes sont fournis par le ViewModel de la page.
/// </summary>
public partial class BottomSheet : ContentView
{
    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(BottomSheet), false,
        propertyChanged: (bindable, _, newValue) => ((BottomSheet)bindable).Animate((bool)newValue));

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(BottomSheet), string.Empty);

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(BottomSheet), string.Empty);

    public static readonly BindableProperty CancelTextProperty =
        BindableProperty.Create(nameof(CancelText), typeof(string), typeof(BottomSheet), "Annuler");

    public static readonly BindableProperty ConfirmTextProperty =
        BindableProperty.Create(nameof(ConfirmText), typeof(string), typeof(BottomSheet), "Confirmer");

    public static readonly BindableProperty CancelCommandProperty =
        BindableProperty.Create(nameof(CancelCommand), typeof(ICommand), typeof(BottomSheet));

    public static readonly BindableProperty ConfirmCommandProperty =
        BindableProperty.Create(nameof(ConfirmCommand), typeof(ICommand), typeof(BottomSheet));

    public BottomSheet()
    {
        InitializeComponent();
    }

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string CancelText { get => (string)GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }
    public string ConfirmText { get => (string)GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public ICommand? ConfirmCommand { get => (ICommand?)GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }

    // Animation purement visuelle (aucune logique métier).
#pragma warning disable CS0618 // FadeTo/TranslateTo : remplacés par *Async en .NET 10 (avertissement seulement)
    private async void Animate(bool open)
    {
        try
        {
            ViewExtensions.CancelAnimations(Scrim);
            ViewExtensions.CancelAnimations(Sheet);

            if (open)
            {
                IsVisible = true;
                Scrim.Opacity = 0;
                Sheet.TranslationY = 420;
                await Task.WhenAll(
                    Scrim.FadeTo(0.45, 200),
                    Sheet.TranslateTo(0, 0, 260, Easing.CubicOut));
            }
            else
            {
                var hidden = Sheet.Height > 0 ? Sheet.Height + 40 : 420;
                await Task.WhenAll(
                    Scrim.FadeTo(0, 180),
                    Sheet.TranslateTo(0, hidden, 220, Easing.CubicIn));
                if (!IsOpen) IsVisible = false;
            }
        }
        catch (Exception)
        {
            IsVisible = open;   // en cas de problème d'animation, on garde au moins le bon état
        }
    }
#pragma warning restore CS0618
}
