using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using HiveShock.Avalonia.ViewModels;

namespace HiveShock.Avalonia.Services;

/// <summary>
/// Icono de HiveShock en la bandeja del sistema (área de notificación en Windows, barra de menús en macOS).
/// Clic: vuelve a mostrar la ventana. Menú: abrir, pausar/reanudar efectos y salir de verdad.
/// El tooltip enseña el estado de la sesión para saber, sin abrir la ventana, si sigue conectado.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly TrayIcon? _tray;
    private readonly NativeMenuItem? _pauseItem;

    /// <summary>
    /// En Linux el icono depende de que el escritorio tenga bandeja (StatusNotifierItem); si no la hay, una ventana
    /// escondida no se podría recuperar. Allí la X minimiza a la barra de tareas en vez de esconder.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public TrayService(MainViewModel vm, Action show, Action quit)
    {
        _vm = vm;
        if (!IsSupported || Application.Current is not { } app)
        {
            return;
        }

        var open = new NativeMenuItem("Abrir HiveShock");
        open.Click += (_, _) => show();
        _pauseItem = new NativeMenuItem(PauseHeader());
        _pauseItem.Click += (_, _) => _vm.ToggleViewerEffectsPauseCommand.Execute(null);
        var exit = new NativeMenuItem("Salir de HiveShock");
        exit.Click += (_, _) => quit();

        _tray = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = TooltipText(),
            Menu = new NativeMenu { Items = { open, _pauseItem, new NativeMenuItemSeparator(), exit } },
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => show();
        TrayIcon.SetIcons(app, [_tray]);
        _vm.PropertyChanged += OnShellChanged;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_tray == null)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.ActiveProfileName))
        {
            _tray.ToolTipText = TooltipText();
        }
        else if (e.PropertyName == nameof(MainViewModel.ViewerEffectsPaused) && _pauseItem != null)
        {
            _pauseItem.Header = PauseHeader();
            _tray.ToolTipText = TooltipText();
        }
    }

    private string PauseHeader() => _vm.ViewerEffectsPaused ? "Reanudar efectos" : "Pausar efectos";

    private string TooltipText()
    {
        // Windows corta el tooltip de la bandeja a 127 caracteres.
        var text = $"HiveShock · {_vm.StatusText} · {_vm.ActiveProfileName}";
        if (_vm.ViewerEffectsPaused)
        {
            text += " · efectos en pausa";
        }

        return text.Length > 120 ? text[..120] + "…" : text;
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            // .ico en Windows (varios tamaños, nítido en la bandeja); PNG en macOS.
            var asset = OperatingSystem.IsWindows() ? "app.ico" : "app.png";
            using var stream = AssetLoader.Open(new Uri($"avares://HiveShock/Assets/{asset}"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _vm.PropertyChanged -= OnShellChanged;
        if (_tray != null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
        }
    }
}
