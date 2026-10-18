using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MediQueue.Patient.ViewModels;

/// <summary>
/// Classe de base : centralise INotifyPropertyChanged exactement comme dans le TP
/// (événement PropertyChanged + [CallerMemberName]).
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? nom = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));

    /// <summary>Affecte le champ puis notifie, seulement si la valeur a changé.</summary>
    protected bool SetProperty<T>(ref T champ, T valeur, [CallerMemberName] string? nom = null)
    {
        if (EqualityComparer<T>.Default.Equals(champ, valeur)) return false;
        champ = valeur;
        OnPropertyChanged(nom);
        return true;
    }
}
