using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Services;

/// <summary>
/// Guardado con espera (debounce) para un grupo de datos editables. Cada editor registra sus
/// colecciones/objetos con <see cref="Track{T}(ObservableCollection{T}, Func{string?, bool}?)"/> y
/// el guardado ocurre solo cuando el usuario deja de tocar (si el guardado automático está activo);
/// si no, queda «Sin guardar» hasta que se pulse el botón. Para un editor nuevo basta con crear otro
/// <see cref="AutoSaver"/> con su <see cref="SaveStatusViewModel"/> y registrar lo que se edita.
/// </summary>
public sealed class AutoSaver
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(900);

    private readonly SaveStatusViewModel _status;
    private readonly Action<bool> _save;
    private readonly string _what;
    private readonly DispatcherTimer _timer;
    private int _suppress;
    private bool _dirty;
    private bool _saving;

    /// <param name="save">Escribe a disco y lanza si falla. Recibe true cuando es automático (sin log de «guardado»).</param>
    /// <param name="what">Cómo se llama lo guardado en la etiqueta («Regalos», «Perfil»…).</param>
    public AutoSaver(SaveStatusViewModel status, Action<bool> save, string what, TimeSpan? delay = null)
    {
        _status = status;
        _save = save;
        _what = what;
        _timer = new DispatcherTimer { Interval = delay ?? DefaultDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Run(automatic: true);
        };
        _status.AutoSaveTurnedOn += () =>
        {
            if (_dirty)
            {
                Run(automatic: true);
            }
        };
    }

    public SaveStatusViewModel Status => _status;

    public bool IsDirty => _dirty;

    /// <summary>Último error de <see cref="SaveNow"/> / guardado automático, para mostrarlo en un diálogo si hace falta.</summary>
    public string? LastError { get; private set; }

    /// <summary>Cambios hechos por código (cargar, normalizar al guardar) no cuentan como edición del usuario.</summary>
    public IDisposable Suppress()
    {
        _suppress++;
        return new Scope(this);
    }

    /// <summary>El usuario cambió algo: marca pendiente y, con guardado automático, programa el guardado.</summary>
    public void NotifyChanged()
    {
        if (_suppress > 0)
        {
            return;
        }

        _dirty = true;
        _status.MarkPending();
        _timer.Stop();
        if (_status.AutoSaveEnabled)
        {
            _timer.Start();
        }
    }

    /// <summary>Guardado inmediato (botón Guardar, cerrar la app). True si quedó en disco.</summary>
    public bool SaveNow() => Run(automatic: false);

    /// <summary>Vuelca lo pendiente si lo hay (al cerrar la app), sin importar la casilla de automático.</summary>
    public void FlushIfDirty()
    {
        _timer.Stop();
        if (_dirty)
        {
            Run(automatic: true);
        }
    }

    public void Track<T>(ObservableCollection<T> list, Func<string?, bool>? ignoreProperty = null)
        where T : class, INotifyPropertyChanged
    {
        foreach (var item in list)
        {
            item.PropertyChanged += (s, e) => OnItemChanged(list, s, e, ignoreProperty);
        }

        list.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (T item in e.NewItems)
                {
                    item.PropertyChanged += (s, args) => OnItemChanged(list, s, args, ignoreProperty);
                }
            }

            NotifyChanged();
        };
    }

    public void Track(INotifyPropertyChanged source, Func<string?, bool>? ignoreProperty = null) =>
        source.PropertyChanged += (_, e) =>
        {
            if (ignoreProperty?.Invoke(e.PropertyName) != true)
            {
                NotifyChanged();
            }
        };

    private void OnItemChanged<T>(
        ObservableCollection<T> list,
        object? sender,
        PropertyChangedEventArgs e,
        Func<string?, bool>? ignoreProperty)
        where T : class
    {
        // Una fila ya quitada de la lista (p. ej. tras Clear) no debe disparar guardados.
        if (ignoreProperty?.Invoke(e.PropertyName) == true || sender is not T item || !list.Contains(item))
        {
            return;
        }

        NotifyChanged();
    }

    private bool Run(bool automatic)
    {
        if (_saving)
        {
            return false;
        }

        _saving = true;
        _timer.Stop();
        _status.MarkSaving();
        try
        {
            using (Suppress())
            {
                _save(automatic);
            }

            _dirty = false;
            LastError = null;
            _status.MarkSaved(_what);
            return true;
        }
        catch (Exception ex)
        {
            // Sigue «sucio»: el siguiente cambio o el botón Guardar lo reintenta.
            LastError = ex.Message;
            _status.MarkFailed(ex.Message);
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    private sealed class Scope(AutoSaver owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                owner._suppress--;
            }
        }
    }
}
