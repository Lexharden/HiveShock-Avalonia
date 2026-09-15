using Android.Content;
using HiveShock;
using HiveShock.Logging;

namespace HiveShock.Android;

internal static class AndroidIntents
{
    public static void Register() => PlatformShell.UrlLauncher = OpenUrl;

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Navegador: {ex.Message}");
        }
    }

    public static void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var context = global::Android.App.Application.Context;
        var clipboard = context.GetSystemService(Context.ClipboardService) as ClipboardManager;
        clipboard?.SetPrimaryClip(ClipData.NewPlainText("HiveShock", text));
    }
}
