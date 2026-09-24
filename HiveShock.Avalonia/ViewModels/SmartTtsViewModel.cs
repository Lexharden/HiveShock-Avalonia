using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Voice;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>
/// Página "Voz": arranca/detiene Smart TTS, elige voz y volumen, configura el atajo de
/// mute y deja probar una frase suelta. <see cref="MainViewModel.SmartTts"/> es la única
/// instancia; Voice puede ser null (Mac/Linux todavía no tienen implementación concreta —
/// ver <see cref="MainViewModel"/>), y la página lo muestra como no disponible.
/// </summary>
public sealed partial class SmartTtsViewModel : ViewModelBase
{
    public SmartTtsViewModel(MainViewModel shell)
    {
        Shell = shell;
        if (Voice == null)
        {
            return;
        }

        var settings = Voice.Settings;
        _enabled = settings.Enabled;
        _voiceId = settings.VoiceId;
        _volume = settings.Volume;
        _muteHotkey = settings.MuteHotkey;

        Voice.MutedChanged += _ => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsMuted)));
        Voice.StreamerSpeakingChanged += _ => Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsStreamerSpeaking)));

        if (_enabled)
        {
            Voice.Start();
        }

        _ = RefreshVoices();
    }

    public MainViewModel Shell { get; }

    private SmartVoiceManager? Voice => Shell.Runtime.Voice;

    public bool IsAvailable => Voice != null;
    public bool IsRunning => Voice?.IsRunning ?? false;
    public bool IsMuted => Voice?.IsMuted ?? false;
    public bool IsStreamerSpeaking => Voice?.IsStreamerSpeaking ?? false;

    public ObservableCollection<VoiceProfile> Voices { get; } = [];

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _voiceId = "";
    [ObservableProperty] private double _volume = 1.0;
    [ObservableProperty] private string _muteHotkey = "Control+Alt+M";
    [ObservableProperty] private string _testText = "Hola, así sonará el chat leído en voz alta.";
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _isRefreshingVoices;

    partial void OnEnabledChanged(bool value)
    {
        if (Voice == null)
        {
            return;
        }

        Voice.Settings.Enabled = value;
        Voice.Settings.Save();
        if (value)
        {
            Voice.Start();
        }
        else
        {
            Voice.Stop();
        }

        OnPropertyChanged(nameof(IsRunning));
    }

    partial void OnVoiceIdChanged(string value)
    {
        if (Voice == null)
        {
            return;
        }

        Voice.Settings.VoiceId = value;
        Voice.Settings.Save();
    }

    partial void OnVolumeChanged(double value)
    {
        if (Voice == null)
        {
            return;
        }

        Voice.Volume = value;
        Voice.Settings.Save();
    }

    [RelayCommand]
    private void SaveHotkey()
    {
        if (Voice == null)
        {
            return;
        }

        try
        {
            Voice.UpdateMuteHotkey(MuteHotkey);
            Voice.Settings.Save();
        }
        catch (Exception ex)
        {
            Shell.Dialogs.Error("Voz", ex.Message);
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (Voice == null)
        {
            return;
        }

        Voice.IsMuted = !Voice.IsMuted;
        OnPropertyChanged(nameof(IsMuted));
    }

    [RelayCommand]
    private async Task Test()
    {
        if (Voice == null || IsTesting || string.IsNullOrWhiteSpace(TestText))
        {
            return;
        }

        IsTesting = true;
        try
        {
            await Voice.PlaySampleAsync(TestText, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Shell.Dialogs.Error("Voz", ex.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task RefreshVoices()
    {
        if (Voice == null || IsRefreshingVoices)
        {
            return;
        }

        IsRefreshingVoices = true;
        try
        {
            var voices = await Voice.ListVoicesAsync(CancellationToken.None).ConfigureAwait(true);
            Voices.Clear();
            foreach (var voice in voices)
            {
                Voices.Add(voice);
            }

            if (string.IsNullOrWhiteSpace(VoiceId))
            {
                var preferred = voices.FirstOrDefault(v => v.Locale.StartsWith("es-", StringComparison.OrdinalIgnoreCase))
                                ?? voices.FirstOrDefault();
                if (preferred != null)
                {
                    VoiceId = preferred.Id;
                }
            }
        }
        catch (Exception ex)
        {
            Shell.Dialogs.Error("Voz", $"No se pudo obtener la lista de voces: {ex.Message}");
        }
        finally
        {
            IsRefreshingVoices = false;
        }
    }
}
