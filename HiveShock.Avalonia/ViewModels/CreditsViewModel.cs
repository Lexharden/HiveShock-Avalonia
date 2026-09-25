using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using HiveShock.Logging;

namespace HiveShock.Avalonia.ViewModels;

/// <summary>Una persona del equipo. <see cref="TikTok"/> sin @; vacío si no se quiere enlazar.</summary>
public sealed partial class CreditMember
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tiktok")]
    public string TikTok { get; set; } = "";

    [JsonIgnore]
    public string Handle => TikTok.Trim().TrimStart('@');

    [JsonIgnore]
    public bool HasTikTok => Handle.Length > 0;

    /// <summary>"Yafel · @yaafel", o solo el nombre.</summary>
    [JsonIgnore]
    public string Label => HasTikTok
        ? string.Equals(Name, Handle, StringComparison.OrdinalIgnoreCase) || Name.Length == 0 ? $"@{Handle}" : $"{Name} · @{Handle}"
        : Name;

    [RelayCommand]
    private void OpenTikTok()
    {
        if (!HasTikTok)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://www.tiktok.com/@{Uri.EscapeDataString(Handle)}",
                UseShellExecute = true,
            });
        }
        catch
        {
            // sin navegador predeterminado
        }
    }
}

/// <summary>Grupo del equipo: "Testers", "Soporte", "Administradores"…</summary>
public sealed class CreditCategory
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("members")]
    public List<CreditMember> Members { get; set; } = [];

    [JsonIgnore]
    public string Header => Icon.Length > 0 ? $"{Icon}  {Title}" : Title;
}

/// <summary>
/// Equipo y agradecimientos de "Acerca de", leído de Assets/credits.json (recurso incrustado).
/// Añadir a alguien es editar ese JSON: categorías y personas, sin tocar la vista ni el código.
/// </summary>
public static class Credits
{
    private sealed class CreditsFile
    {
        [JsonPropertyName("categories")]
        public List<CreditCategory> Categories { get; set; } = [];
    }

    public static IReadOnlyList<CreditCategory> Load()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://HiveShock/Assets/credits.json"));
            var file = JsonSerializer.Deserialize<CreditsFile>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return (file?.Categories ?? [])
                .Select(c =>
                {
                    c.Members = c.Members.Where(m => !string.IsNullOrWhiteSpace(m.Name) || m.HasTikTok).ToList();
                    return c;
                })
                .Where(c => c.Members.Count > 0 && !string.IsNullOrWhiteSpace(c.Title))
                .ToList();
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Créditos: no se pudo leer credits.json: {ex.Message}");
            return [];
        }
    }
}
