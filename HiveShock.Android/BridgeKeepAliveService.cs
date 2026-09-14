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
    public const string ChannelId = "hiveshock.live.v2";
    public const int NotificationId = 43000;

    private PowerManager.WakeLock? _wakeLock;

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

        AcquireWakeLock();
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        ReleaseWakeLock();
        base.OnDestroy();
    }

    private void AcquireWakeLock()
    {
        if (_wakeLock is { IsHeld: true })
        {
            return;
        }

        var pm = GetSystemService(PowerService) as PowerManager;
        _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "HiveShock:Bridge");
        if (_wakeLock is null)
        {
            return;
        }

        _wakeLock.SetReferenceCounted(false);
        _wakeLock.Acquire();
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock is { IsHeld: true })
        {
            _wakeLock.Release();
        }

        _wakeLock = null;
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

        var channel = new NotificationChannel(ChannelId, "HiveShock en segundo plano", NotificationImportance.Default)
        {
            Description = "Icono fijo en la barra mientras el live está conectado.",
            LockscreenVisibility = NotificationVisibility.Public,
        };
        channel.SetShowBadge(true);
        channel.EnableVibration(false);
        channel.SetSound(null, null);
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

        builder
            .SetContentTitle("HiveShock en segundo plano")
            .SetContentText("Live activo. Toca para volver.")
            .SetSmallIcon(Resource.Drawable.icon)
            .SetLargeIcon(global::Android.Graphics.BitmapFactory.DecodeResource(Resources, Resource.Drawable.icon))
            .SetColor(unchecked((int)0xFFEBA00A))
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(true)
            .SetUsesChronometer(true)
            .SetContentIntent(pending)
            .SetCategory(Notification.CategoryService)
            .SetVisibility(NotificationVisibility.Public);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            builder.SetForegroundServiceBehavior((int)NotificationForegroundService.Immediate);
        }

        return builder.Build();
    }
}
