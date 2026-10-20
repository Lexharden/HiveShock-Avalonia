using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Hosting;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Efectos del sistema (hoy: reiniciar el equipo). Armado por sesión, con cuenta atrás visible y cancelable.</summary>
public sealed partial class SystemEffectsViewModel : ViewModelBase
{
    private const string ConfirmWord = "REINICIAR";

    private readonly MainViewModel _shell;
    private readonly SystemEffectService _service;
    private bool _loading;

    public SystemEffectsViewModel(MainViewModel shell)
    {
        _shell = shell;
        _service = shell.Runtime.SystemEffects;
        _service.Changed += () => Dispatcher.UIThread.Post(Refresh);
        _loading = true;
        CountdownText = _service.Settings.ClampedCountdown.ToString(CultureInfo.InvariantCulture);
        Force = _service.Settings.Force;
        _loading = false;
        Refresh();
    }

    [ObservableProperty] private string _countdownText = "";
    [ObservableProperty] private bool _force;
    [ObservableProperty] private string _message = "";

    public bool IsSupported => _service.IsSupported;

    public bool Armed => _service.Armed;

    public bool IsCounting => _service.IsCountingDown;

    public string ArmLabel => Armed ? "Desarmar el reinicio" : "Armar el reinicio";

    public string StatusText => IsCounting
        ? $"Reiniciando en {_service.SecondsLeft} s ({_service.TriggeredBy ?? "alguien"})"
        : Armed
            ? "ARMADO en esta sesión: el regalo o comando asignado reiniciará el equipo."
            : "Desarmado. Nada puede reiniciar el equipo.";

    public string BannerText =>
        $"⚠ El equipo se reiniciará en {_service.SecondsLeft} s ({_service.TriggeredBy ?? "alguien"}). " +
        $"Cancela con {SystemEffectService.CancelHotkey} o con el botón.";

    partial void OnCountdownTextChanged(string value) => SaveSettings();

    partial void OnForceChanged(bool value) => SaveSettings();

    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        if (int.TryParse(CountdownText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            _service.Configure(seconds, Force);
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Armed));
        OnPropertyChanged(nameof(IsCounting));
        OnPropertyChanged(nameof(ArmLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(BannerText));
    }

    [RelayCommand]
    private async Task ToggleArmAsync()
    {
        if (Armed)
        {
            _service.SetArmed(false);
            Message = "";
            return;
        }

        var seconds = _service.Settings.ClampedCountdown;
        var confirmed = await _shell.Dialogs.ConfirmTypedAsync(
            "Armar el reinicio del equipo",
            $"Mientras esté armado, el efecto «Reiniciar el equipo» reiniciará ESTE equipo tras una cuenta atrás de {seconds} s" +
            $"{(Force ? " y cerrará los programas sin guardar" : "")}. Solo vale en esta sesión. " +
            $"Escribe {ConfirmWord} para armarlo.",
            ConfirmWord);
        if (!confirmed)
        {
            return;
        }

        _service.SetArmed(true);
        Message = _shell.Gifts.EnsureRestartGift();
    }

    [RelayCommand]
    private void Preview()
    {
        Message = _service.Trigger(SystemEffectIds.RestartPc, "tú", "prueba", preview: true)
            ? "Cuenta atrás de prueba: no se reiniciará nada."
            : "No se pudo iniciar la prueba (¿ya hay una cuenta atrás?).";
    }

    [RelayCommand]
    private void CancelRestart() => _service.Cancel("cancelado desde HiveShock");
}
