using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;

namespace HiveShock.Android;

internal static class AndroidLivePermissions
{
    public const int NotifyRequest = 1001;

    public static void Request(Activity activity)
    {
        RequestNotifications(activity);
        PromptBatteryExemption(activity);
    }

    public static void RequestNotifications(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        if (activity.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return;
        }

        activity.RequestPermissions([Manifest.Permission.PostNotifications], NotifyRequest);
    }

    public static void PromptBatteryExemption(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            return;
        }

        var pm = activity.GetSystemService(Context.PowerService) as PowerManager;
        if (pm is null || activity.PackageName is null)
        {
            return;
        }

        if (pm.IsIgnoringBatteryOptimizations(activity.PackageName))
        {
            return;
        }

        var prefs = activity.GetSharedPreferences("hiveshock.live", FileCreationMode.Private);
        if (prefs?.GetBoolean("asked_battery", false) == true)
        {
            return;
        }

        prefs?.Edit()?.PutBoolean("asked_battery", true)?.Apply();

        try
        {
            var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            activity.StartActivity(intent);
        }
        catch
        {
            // el fabricante puede no exponer el diálogo
        }
    }
}
