using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace HiveShock.Avalonia.Themes;

public static class ThemeManager
{
    public static readonly Color BrandBlue = Color.Parse("#075BAA");
    public static readonly Color BrandGold = Color.Parse("#EBA00A");

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static event Action<AppTheme>? ThemeChanged;

    private static Application? _app;
    private static bool _followSystem;
    private static bool _watching;

    public static void Apply(Application app, AppTheme theme, bool followSystem = false)
    {
        _app = app;
        _followSystem = followSystem;
        Watch(app);

        app.RequestedThemeVariant = followSystem
            ? ThemeVariant.Default
            : theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;

        ApplyBrushes(app, followSystem ? DetectSystemTheme() : theme);
    }

    public static AppTheme DetectSystemTheme()
    {
        try
        {
            var values = Application.Current?.PlatformSettings?.GetColorValues();
            if (values != null)
            {
                if (values.ThemeVariant == PlatformThemeVariant.Light)
                {
                    return AppTheme.Light;
                }

                if (values.ThemeVariant == PlatformThemeVariant.Dark)
                {
                    return AppTheme.Dark;
                }
            }
        }
        catch
        {
            // fallback
        }

        try
        {
            if (Application.Current?.ActualThemeVariant == ThemeVariant.Light)
            {
                return AppTheme.Light;
            }
        }
        catch
        {
            // dark
        }

        return AppTheme.Dark;
    }

    private static IPlatformSettings? _watchedSettings;

    private static void Watch(Application app)
    {
        if (!_watching)
        {
            _watching = true;
            app.ActualThemeVariantChanged += (_, _) => OnOsThemeChanged();
        }

        var settings = app.PlatformSettings;
        if (settings != null && !ReferenceEquals(settings, _watchedSettings))
        {
            _watchedSettings = settings;
            settings.ColorValuesChanged += (_, _) => OnOsThemeChanged();
        }
    }

    private static void OnOsThemeChanged()
    {
        if (!_followSystem || _app == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_followSystem || _app == null)
            {
                return;
            }

            var next = DetectSystemTheme();
            if (next == Current)
            {
                return;
            }

            ApplyBrushes(_app, next);
        });
    }

    private static void ApplyBrushes(Application app, AppTheme theme)
    {
        Current = theme;
        var r = app.Resources;
        if (theme == AppTheme.Light)
        {
            Set(r, "BgBrush", "#EEF2F8");
            Set(r, "CardBrush", "#F7F9FC");
            Set(r, "CardAltBrush", "#E2E9F3");
            Set(r, "BorderBrush", "#B8C6DB");
            Set(r, "PopupBrush", "#F7F9FC");
            Set(r, "TextBrush", "#0E1520");
            Set(r, "MutedBrush", "#5C6B82");
            Set(r, "AccentBrush", "#075BAA");
            Set(r, "AccentHoverBrush", "#054A8C");
            Set(r, "AccentForegroundBrush", "#FFFFFF");
            Set(r, "SelectionBrush", "#D9E8F6");
            Set(r, "DangerBrush", "#C45C5C");
            Set(r, "StatusOkBrush", "#2F7358");
            Set(r, "LiveBrush", "#15803D");
            Set(r, "LiveTextBrush", "#146C34");
            Set(r, "LiveOnForegroundBrush", "#F4FFF7");
            Set(r, "InputBrush", "#FFFFFF");
            Set(r, "GlyphBrush", "#075BAA");
            Set(r, "StatusOnForegroundBrush", "#1A1200");
        }
        else
        {
            Set(r, "BgBrush", "#070A10");
            Set(r, "CardBrush", "#0D121A");
            Set(r, "CardAltBrush", "#141B28");
            Set(r, "BorderBrush", "#243044");
            Set(r, "PopupBrush", "#101722");
            Set(r, "TextBrush", "#E8EEF8");
            Set(r, "MutedBrush", "#8B9BB3");
            Set(r, "AccentBrush", "#075BAA");
            Set(r, "AccentHoverBrush", "#0A74D4");
            Set(r, "AccentForegroundBrush", "#F2F7FF");
            Set(r, "SelectionBrush", "#0A3058");
            Set(r, "DangerBrush", "#E07A7A");
            Set(r, "StatusOkBrush", "#3F8F6E");
            Set(r, "LiveBrush", "#15803D");
            Set(r, "LiveTextBrush", "#4ADE80");
            Set(r, "LiveOnForegroundBrush", "#F4FFF7");
            Set(r, "InputBrush", "#0F1520");
            Set(r, "GlyphBrush", "#EBA00A");
            Set(r, "StatusOnForegroundBrush", "#1A1200");
        }

        Set(r, "BrandBlueBrush", "#075BAA");
        Set(r, "BrandGoldBrush", "#EBA00A");
        r["BrandLogo"] = LoadBrandLogo(theme);
        ThemeChanged?.Invoke(theme);
    }

    public static Color OverlayCard(AppTheme theme) =>
        theme == AppTheme.Light ? Color.Parse("#F0F4FA") : Color.Parse("#0C121C");

    public static Color OverlayBorder(AppTheme theme) =>
        theme == AppTheme.Light ? Color.Parse("#B8C6DB") : Color.Parse("#2A3D5C");

    public static Color OverlayText(AppTheme theme) =>
        theme == AppTheme.Light ? Color.Parse("#0E1520") : Color.Parse("#E8EEF8");

    public static Color OverlayMuted(AppTheme theme) =>
        theme == AppTheme.Light ? Color.Parse("#5C6B82") : Color.Parse("#8B9BB3");

    public static Color OverlayAccent(AppTheme theme) => BrandGold;

    public static Color OverlayHighlight(AppTheme theme) =>
        theme == AppTheme.Light ? Color.Parse("#E8F1FA") : Color.Parse("#0A3058");

    public static Bitmap? LoadBrandLogo(AppTheme theme)
    {
        var path = theme == AppTheme.Light
            ? "avares://HiveShock/Assets/logo-light.png"
            : "avares://HiveShock/Assets/logo-dark.png";
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri(path)));
        }
        catch
        {
            try
            {
                var fallback = theme == AppTheme.Light
                    ? "avares://HiveShock/Assets/logo-dark.png"
                    : "avares://HiveShock/Assets/logo-light.png";
                return new Bitmap(AssetLoader.Open(new Uri(fallback)));
            }
            catch
            {
                return null;
            }
        }
    }

    private static void Set(IResourceDictionary resources, string key, string hex)
    {
        resources[key] = new SolidColorBrush(Color.Parse(hex));
    }
}
