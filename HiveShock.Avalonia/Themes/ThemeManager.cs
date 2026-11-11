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
        // Tokens de color. Capas de superficie de abajo hacia arriba:
        //   Bg (fondo de la app) < Sidebar/TitleBar < Card (paneles) < CardAlt (filas, campos anidados) < Popup.
        // Los nombres antiguos (CardBrush, MutedBrush…) se conservan para no romper vistas ni ventanas de overlay.
        if (theme == AppTheme.Light)
        {
            Set(r, "BgBrush", "#F3F5F9");
            Set(r, "SidebarBrush", "#EBEEF4");
            Set(r, "TitleBarBrush", "#EBEEF4");
            Set(r, "CardBrush", "#FFFFFF");
            Set(r, "CardAltBrush", "#F1F3F8");
            Set(r, "HoverBrush", "#E4E8F0");
            Set(r, "PressedBrush", "#D9DEE8");
            Set(r, "BorderBrush", "#DCE1EA");
            Set(r, "StrongBorderBrush", "#C3CAD6");
            Set(r, "PopupBrush", "#FFFFFF");
            Set(r, "TextBrush", "#121826");
            Set(r, "MutedBrush", "#5A6478");
            Set(r, "FaintBrush", "#8C95A6");
            Set(r, "AccentBrush", "#075BAA");
            Set(r, "AccentHoverBrush", "#0A4F92");
            Set(r, "AccentPressedBrush", "#08447D");
            Set(r, "AccentForegroundBrush", "#FFFFFF");
            Set(r, "AccentTextBrush", "#075BAA");
            Set(r, "AccentSubtleBrush", "#E3EEFA");
            Set(r, "SelectionBrush", "#E3EEFA");
            Set(r, "GoldTextBrush", "#9A6500");
            Set(r, "GoldSubtleBrush", "#FDF1D8");
            Set(r, "DangerBrush", "#C03E3E");
            Set(r, "DangerHoverBrush", "#A83434");
            Set(r, "DangerSubtleBrush", "#FBE7E7");
            Set(r, "StatusOkBrush", "#1F8A5B");
            Set(r, "OkSubtleBrush", "#E2F4EB");
            Set(r, "LiveBrush", "#15803D");
            Set(r, "LiveTextBrush", "#146C34");
            Set(r, "LiveOnForegroundBrush", "#F4FFF7");
            Set(r, "InputBrush", "#FFFFFF");
            Set(r, "GlyphBrush", "#075BAA");
            Set(r, "StatusOnForegroundBrush", "#1A1200");
            Set(r, "ScrimBrush", "#33000000");
        }
        else
        {
            Set(r, "BgBrush", "#0B0E14");
            Set(r, "SidebarBrush", "#0F131A");
            Set(r, "TitleBarBrush", "#0F131A");
            Set(r, "CardBrush", "#141922");
            Set(r, "CardAltBrush", "#1B212C");
            Set(r, "HoverBrush", "#212836");
            Set(r, "PressedBrush", "#283040");
            Set(r, "BorderBrush", "#242B38");
            Set(r, "StrongBorderBrush", "#333C4D");
            Set(r, "PopupBrush", "#1A202B");
            Set(r, "TextBrush", "#E7EBF3");
            Set(r, "MutedBrush", "#8D97AA");
            Set(r, "FaintBrush", "#5E687B");
            Set(r, "AccentBrush", "#1A6FD1");
            Set(r, "AccentHoverBrush", "#2A80E3");
            Set(r, "AccentPressedBrush", "#1660B6");
            Set(r, "AccentForegroundBrush", "#FFFFFF");
            Set(r, "AccentTextBrush", "#5AA2F2");
            Set(r, "AccentSubtleBrush", "#15253B");
            Set(r, "SelectionBrush", "#172840");
            Set(r, "GoldTextBrush", "#F2B63D");
            Set(r, "GoldSubtleBrush", "#2A2211");
            Set(r, "DangerBrush", "#E05656");
            Set(r, "DangerHoverBrush", "#EA6B6B");
            Set(r, "DangerSubtleBrush", "#2E1719");
            Set(r, "StatusOkBrush", "#3BA776");
            Set(r, "OkSubtleBrush", "#13271F");
            Set(r, "LiveBrush", "#16A34A");
            Set(r, "LiveTextBrush", "#4ADE80");
            Set(r, "LiveOnForegroundBrush", "#F4FFF7");
            Set(r, "InputBrush", "#10141C");
            Set(r, "GlyphBrush", "#EBA00A");
            Set(r, "StatusOnForegroundBrush", "#1A1200");
            Set(r, "ScrimBrush", "#66000000");
        }

        // Recursos de Fluent que no siguen la paleta (fondos de Expander, menús, desplegables…):
        // se apuntan a los tokens propios para que todo use las mismas superficies.
        Alias(r, "ExpanderHeaderBackground", "CardBrush");
        Alias(r, "ExpanderHeaderBackgroundPointerOver", "HoverBrush");
        Alias(r, "ExpanderHeaderBackgroundPressed", "PressedBrush");
        Alias(r, "ExpanderHeaderBackgroundDisabled", "CardBrush");
        Alias(r, "ExpanderHeaderBorderBrush", "BorderBrush");
        Alias(r, "ExpanderHeaderBorderBrushPointerOver", "StrongBorderBrush");
        Alias(r, "ExpanderHeaderBorderBrushPressed", "StrongBorderBrush");
        Alias(r, "ExpanderContentBackground", "CardBrush");
        Alias(r, "ExpanderContentBorderBrush", "BorderBrush");
        Alias(r, "ComboBoxDropDownBackground", "PopupBrush");
        Alias(r, "ComboBoxDropDownBorderBrush", "StrongBorderBrush");
        Alias(r, "MenuFlyoutPresenterBackground", "PopupBrush");
        Alias(r, "MenuFlyoutPresenterBorderBrush", "StrongBorderBrush");
        Alias(r, "FlyoutPresenterBackground", "PopupBrush");
        Alias(r, "FlyoutBorderThemeBrush", "StrongBorderBrush");
        Alias(r, "MenuFlyoutItemBackgroundPointerOver", "HoverBrush");
        Alias(r, "ComboBoxItemBackgroundPointerOver", "HoverBrush");
        Alias(r, "ComboBoxItemBackgroundSelected", "SelectionBrush");
        Alias(r, "ComboBoxItemBackgroundSelectedPointerOver", "SelectionBrush");
        Alias(r, "ButtonBackground", "CardAltBrush");
        Alias(r, "ButtonBackgroundPointerOver", "HoverBrush");
        Alias(r, "ButtonBackgroundPressed", "PressedBrush");
        Alias(r, "ButtonBorderBrush", "BorderBrush");
        Alias(r, "RepeatButtonBackground", "CardAltBrush");
        Alias(r, "RepeatButtonBackgroundPointerOver", "HoverBrush");
        Alias(r, "RepeatButtonBackgroundPressed", "PressedBrush");
        Alias(r, "RepeatButtonBorderBrush", "BorderBrush");

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

    private static void Alias(IResourceDictionary resources, string key, string tokenKey)
    {
        if (resources.TryGetValue(tokenKey, out var brush))
        {
            resources[key] = brush;
        }
    }

    private static void Set(IResourceDictionary resources, string key, string hex)
    {
        resources[key] = new SolidColorBrush(Color.Parse(hex));
    }
}
