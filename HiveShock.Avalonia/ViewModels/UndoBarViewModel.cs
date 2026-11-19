using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>
/// Aviso «Regalo «GG» eliminado · Deshacer» que aparece abajo unos segundos tras borrar.
/// Borrar no pregunta: es rápido en directo y, si fue sin querer, se deshace (botón o Ctrl+Z).
/// Solo se guarda el último borrado; uno nuevo confirma el anterior. Si la lista se recarga de disco
/// (otro perfil, volver a la página) el aviso se descarta, porque las filas guardadas ya no son las de pantalla.
/// </summary>
public sealed partial class UndoBarViewModel : ViewModelBase
{
    private static readonly TimeSpan Visible = TimeSpan.FromSeconds(8);

    private readonly DispatcherTimer _timer;
    private Action? _undo;

    public UndoBarViewModel()
    {
        _timer = new DispatcherTimer { Interval = Visible };
        _timer.Tick += (_, _) => Dismiss();
    }

    [ObservableProperty] private string _message = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _isOpen;

    /// <summary>Muestra el aviso con la forma de deshacer lo que se acaba de hacer.</summary>
    public void Offer(string message, Action undo)
    {
        _undo = undo;
        Message = message;
        IsOpen = true;
        _timer.Stop();
        _timer.Start();
    }

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Undo()
    {
        var undo = _undo;
        Dismiss();
        undo?.Invoke();
    }

    /// <summary>Cierra el aviso; lo borrado queda borrado.</summary>
    [RelayCommand]
    public void Dismiss()
    {
        _timer.Stop();
        _undo = null;
        IsOpen = false;
    }

    /// <summary>
    /// Qué borrar: todos los seleccionados si hay varios (Ctrl/Mayús+clic); si no, el que se está editando.
    /// </summary>
    public static IReadOnlyList<T> Targets<T>(IEnumerable<T> selected, T? current) where T : class
    {
        var many = selected.ToList();
        return many.Count > 1 ? many : current is null ? [] : [current];
    }

    /// <summary>
    /// Quita <paramref name="items"/> de <paramref name="list"/> y ofrece deshacerlo, devolviendo cada uno a su sitio.
    /// </summary>
    /// <param name="single">Texto para un solo elemento, p. ej. <c>r => $"Regalo «{r.Name}» eliminado"</c>.</param>
    /// <param name="plural">Texto para varios, p. ej. <c>n => $"{n} regalos eliminados"</c>.</param>
    /// <param name="select">Cómo seleccionar una fila (tras borrar, la vecina; tras deshacer, la recuperada).</param>
    /// <param name="changed">Opcional: qué refrescar después (contadores, estados derivados…).</param>
    public void RemoveWithUndo<T>(
        ObservableCollection<T> list,
        IEnumerable<T> items,
        Func<T, string> single,
        Func<int, string> plural,
        Action<T?> select,
        Action? changed = null)
        where T : class
    {
        var removed = items
            .Distinct()
            .Select(item => (Index: list.IndexOf(item), Item: item))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .ToList();
        if (removed.Count == 0)
        {
            return;
        }

        // De atrás hacia delante para que los índices guardados sigan valiendo al deshacer.
        for (var i = removed.Count - 1; i >= 0; i--)
        {
            list.RemoveAt(removed[i].Index);
        }

        select(list.Count == 0 ? null : list[Math.Clamp(removed[0].Index, 0, list.Count - 1)]);
        changed?.Invoke();

        Offer(removed.Count == 1 ? single(removed[0].Item) : plural(removed.Count), () =>
        {
            foreach (var (index, item) in removed)
            {
                list.Insert(Math.Min(index, list.Count), item);
            }

            select(removed[0].Item);
            changed?.Invoke();
        });
    }
}
