using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HiveShock.Voice;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>
/// Fila del desplegable de voces: o una cabecera de idioma (no seleccionable) o una voz.
/// Avalonia no agrupa ComboBox por sí solo, así que las cabeceras van como filas más.
/// </summary>
public sealed class VoiceOption
{
    private VoiceOption(VoiceProfile? voice, string text, string fullText)
    {
        Voice = voice;
        Text = text;
        FullText = fullText;
    }

    public VoiceProfile? Voice { get; }
    public bool IsHeader => Voice == null;

    /// <summary>En la lista: "Español" (cabecera) o "Dalia · México" (voz, el idioma ya está en la cabecera).</summary>
    public string Text { get; }

    /// <summary>En la caja cerrada, sin cabecera a la vista: "Dalia · Español (México)".</summary>
    public string FullText { get; }

    public static VoiceOption Header(string languageName) => new(null, languageName, languageName);

    public static VoiceOption For(VoiceProfile voice)
    {
        var language = VoiceLanguages.LanguageName(VoiceLanguages.LanguageCode(voice.Locale));
        var region = VoiceLanguages.RegionName(voice.Locale);
        return new VoiceOption(
            voice,
            region.Length > 0 ? $"{voice.DisplayName} · {region}" : voice.DisplayName,
            region.Length > 0 ? $"{voice.DisplayName} · {language} ({region})" : $"{voice.DisplayName} · {language}");
    }

    /// <summary>Lista agrupada: español primero, luego idiomas por nombre; dentro, por país y nombre.</summary>
    public static List<VoiceOption> Grouped(IEnumerable<VoiceProfile> voices)
    {
        var result = new List<VoiceOption>();
        var groups = voices
            .GroupBy(v => VoiceLanguages.LanguageCode(v.Locale))
            .OrderBy(g => VoiceLanguages.SortKey(g.Key))
            .ThenBy(g => VoiceLanguages.LanguageName(g.Key), StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in groups)
        {
            result.Add(Header(VoiceLanguages.LanguageName(group.Key)));
            result.AddRange(group
                .OrderBy(v => VoiceLanguages.RegionName(v.Locale), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(For));
        }

        return result;
    }
}

/// <summary>Idioma en la lista del sorteo de voz aleatoria, con sus voces desplegables.</summary>
public sealed partial class RandomLanguageItem : ObservableObject
{
    private readonly Action _changed;

    public RandomLanguageItem(string code, IEnumerable<VoiceProfile> voices, bool selected,
        ISet<string> excluded, Action changed)
    {
        Code = code;
        Name = VoiceLanguages.LanguageName(code);
        _changed = changed;
        _isSelected = selected;
        foreach (var voice in voices
                     .OrderBy(v => VoiceLanguages.RegionName(v.Locale), StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Voices.Add(new RandomVoiceItem(this, voice, !excluded.Contains(voice.Id)));
        }
    }

    public string Code { get; }
    public string Name { get; }
    public ObservableCollection<RandomVoiceItem> Voices { get; } = [];

    [ObservableProperty] private bool _isSelected;

    public int IncludedCount => Voices.Count(v => v.IsIncluded);

    public string CountText => !IsSelected
        ? $"{Voices.Count} voces"
        : IncludedCount == Voices.Count
            ? $"todas sus voces ({Voices.Count})"
            : $"{IncludedCount} de {Voices.Count} voces";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CountText));
        _changed();
    }

    internal void OnVoiceToggled()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(CountText));
        _changed();
    }
}

/// <summary>Voz dentro de un idioma del sorteo: se puede desmarcar sin quitar el idioma entero.</summary>
public sealed partial class RandomVoiceItem : ObservableObject
{
    private readonly RandomLanguageItem _language;

    public RandomVoiceItem(RandomLanguageItem language, VoiceProfile voice, bool included)
    {
        _language = language;
        Voice = voice;
        _isIncluded = included;
        var region = VoiceLanguages.RegionName(voice.Locale);
        Text = region.Length > 0 ? $"{voice.DisplayName} · {region}" : voice.DisplayName;
    }

    public VoiceProfile Voice { get; }
    public string Text { get; }

    [ObservableProperty] private bool _isIncluded;

    partial void OnIsIncludedChanged(bool value) => _language.OnVoiceToggled();
}
