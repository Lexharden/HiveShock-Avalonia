using CommunityToolkit.Mvvm.ComponentModel;

namespace HiveShock.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Asigna el id de efecto elegido en un selector. Los ComboBox con SelectedValue devuelven null cuando se
    /// quedan sin opciones (cambiar de página, lista de efectos nueva): eso no es una decisión del usuario y,
    /// si se aceptara, el guardado automático borraría el comando. Se ignora y se vuelve a pintar el valor real.
    /// «Ninguno» llega como "" y sí se respeta.
    /// </summary>
    protected bool SetEffectId(
        ref string field,
        string? value,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (value is null)
        {
            OnPropertyChanged(name);
            return false;
        }

        return SetProperty(ref field, value, name);
    }
}
