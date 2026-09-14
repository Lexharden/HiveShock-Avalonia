using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace HiveShock.Android;

/// <summary>
/// Servicio en primer plano: notificación fija mientras hay sesión live.
/// Android deja de matar el proceso al cambiar de app o apagar la pantalla.
/// </summary>
[Service(
    Name = "dev.yafel.hiveshock.BridgeKeepAliveService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class BridgeKeepAliveService : Service
{
    public const string ChannelId = "hiveshock.bridge";
    public const int NotificationId = 43000;

    public static void Start()
    {
        var context = global::Android.App.Application.Context;
        void Go()
        {
            var intent = new Intent(context, typeof(BridgeKeepAliveService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }

        var main = Looper.MainLooper;
        if (main is null || main.IsCurrentThread)
        {
            Go();
            return;
        }

        new Handler(main).Post(Go);
    }

    public static void Stop()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(BridgeKeepAliveService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.NotSticky;
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "HiveShock conectado", NotificationImportance.Low)
        {
            Description = "Se queda visible mientras el teléfono está unido al live.",
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var launch = new Intent(this, typeof(MainActivity));
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
        var pendingFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            pendingFlags |= PendingIntentFlags.Immutable;
        }

        var pending = PendingIntent.GetActivity(this, 0, launch, pendingFlags);

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);

        return builder
            .SetContentTitle("HiveShock conectado")
            .SetContentText("El live sigue en segundo plano. Toca para volver.")
            .SetSmallIcon(Resource.Drawable.ic_stat_hiveshock)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(pending)
            .SetCategory(Notification.CategoryService)
            .Build();
    }
}
