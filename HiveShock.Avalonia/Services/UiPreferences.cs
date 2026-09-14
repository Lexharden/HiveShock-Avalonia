using System.IO;
using System.Text.Json;
using HiveShock.Avalonia.Themes;
using HiveShock.Configuration;
using HiveShock.Hosting;

namespace HiveShock.Avalonia.Services;

public sealed class UiPreferences
{
    public string Theme { get; set; } = "system";
    public string DeathCounterMode { get; set; } = "count_up";
    public int DeathCounterStart { get; set; }
    public int? DeathCounterValue { get; set; }
    public bool DeathOverlayEnabled { get; set; }
    public bool GiftOverlayEnabled { get; set; }
    public bool GoalOverlayEnabled { get; set; }
    public string OverlayTitle { get; set; } = "";
    public double OverlayLeft { get; set; } = double.NaN;
    public double OverlayTop { get; set; } = double.NaN;
    public double GiftOverlayLeft { get; set; } = double.NaN;
    public double GiftOverlayTop { get; set; } = double.NaN;
    public double GoalOverlayLeft { get; set; } = double.NaN;
    public double GoalOverlayTop { get; set; } = double.NaN;
    public double OverlayScale { get; set; } = 1.5;
    public bool DetailLogOpen { get; set; }
    public bool NavExpanded { get; set; } = true;

    public string GiftsOverlayTitle { get; set; } = "Regalos";
    public bool ShowGiftsOverlayTitle { get; set; } = true;
    public bool OverlayShowBackground { get; set; } = true;
    public double OverlayBackgroundOpacity { get; set; } = 0.94;
    public double OverlayNumberSize { get; set; } = 72;
    public double OverlayTitleSize { get; set; } = 16;
    public double OverlayGiftNameSize { get; set; } = 14;
    public string OverlayNumberColor { get; set; } = "";
    public string OverlayTitleColor { get; set; } = "";
    public string OverlayGiftColor { get; set; } = "";
    public string OverlayBackgroundColor { get; set; } = "";
    public string OverlayAlign { get; set; } = "center";

    private static string Path => System.IO.Path.Combine(AppPaths.AppDirectory, ".hiveshock-ui.json");
    private static string LegacyPath => System.IO.Path.Combine(AppPaths.AppDirectory, ".bridge-ui.json");

    public static UiPreferences Load()
    {
        try
        {
            var path = File.Exists(Path) ? Path : LegacyPath;
            if (!File.Exists(path))
            {
                return new UiPreferences();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiPreferences>(json) ?? new UiPreferences();
        }
        catch
        {
            return new UiPreferences();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
    }

    public bool FollowsSystem =>
        !string.Equals(Theme, "dark", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Theme, "light", StringComparison.OrdinalIgnoreCase);

    public AppTheme ResolveTheme() => Theme switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => ThemeManager.DetectSystemTheme(),
    };

    public HiveShock.Hosting.DeathCounterMode ResolveDeathMode() =>
        string.Equals(DeathCounterMode, "lives", StringComparison.OrdinalIgnoreCase)
            ? HiveShock.Hosting.DeathCounterMode.Lives
            : HiveShock.Hosting.DeathCounterMode.CountUp;

    public int ResolveDeathStart()
    {
        if (ResolveDeathMode() == HiveShock.Hosting.DeathCounterMode.Lives)
        {
            return DeathCounterStart > 0 ? DeathCounterStart : 500;
        }

        return Math.Max(0, DeathCounterStart);
    }

    public string ResolveOverlayTitle()
    {
        var custom = OverlayTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (string.Equals(custom, "muertes", StringComparison.OrdinalIgnoreCase))
            {
                return "Muertes";
            }

            if (string.Equals(custom, "vidas", StringComparison.OrdinalIgnoreCase))
            {
                return "Vidas";
            }

            return custom;
        }

        return ResolveDeathMode() == HiveShock.Hosting.DeathCounterMode.Lives ? "Vidas" : "Muertes";
    }

    public void ResetOverlayLook()
    {
        OverlayScale = 1.5;
        OverlayTitle = "";
        GiftsOverlayTitle = "Regalos";
        ShowGiftsOverlayTitle = true;
        OverlayShowBackground = true;
        OverlayBackgroundOpacity = 0.94;
        OverlayNumberSize = 72;
        OverlayTitleSize = 16;
        OverlayGiftNameSize = 14;
        OverlayNumberColor = "";
        OverlayTitleColor = "";
        OverlayGiftColor = "";
        OverlayBackgroundColor = "";
        OverlayAlign = "center";
    }

    public int ResolveDeathValueToRestore()
    {
        var start = ResolveDeathStart();
        return DeathCounterValue ?? start;
    }

    public double ResolveOverlayScale()
    {
        if (double.IsNaN(OverlayScale) || OverlayScale <= 0)
        {
            return 1.5;
        }

        return Math.Clamp(OverlayScale, 1.0, 3.0);
    }
}
