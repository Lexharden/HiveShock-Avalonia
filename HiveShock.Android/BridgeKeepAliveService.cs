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

    /// <summary>Cota dura del wake lock: si algo impide liberarlo, no se queda encendido para siempre.</summary>
    private static readonly TimeSpan WakeLockTimeout = TimeSpan.FromHours(6);
    /// <summary>Renovar bastante antes de la cota para no dejar un hueco sin wake lock.</summary>
    private static readonly TimeSpan WakeLockRenewInterval = TimeSpan.FromMinutes(30);

    private PowerManager.WakeLock? _wakeLock;
    private Handler? _renewHandler;
    private Action? _renewAction;

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
        StopWakeLockRenewal();
        ReleaseWakeLock();
        base.OnDestroy();
    }

    private void AcquireWakeLock()
    {
        if (_wakeLock is null)
        {
            var pm = GetSystemService(PowerService) as PowerManager;
            _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "HiveShock:Bridge");
            _wakeLock?.SetReferenceCounted(false);
        }

        if (_wakeLock is null)
        {
            return;
        }

        // Con timeout: si por lo que sea nunca llega OnDestroy, el sistema lo suelta solo.
        _wakeLock.Acquire((long)WakeLockTimeout.TotalMilliseconds);
        StartWakeLockRenewal();
    }

    /// <summary>Vuelve a pedir el wake lock antes de que caduque, mientras el servicio siga vivo.</summary>
    private void StartWakeLockRenewal()
    {
        _renewHandler ??= new Handler(Looper.MainLooper!);
        _renewAction ??= () =>
        {
            if (_wakeLock is { } wakeLock)
            {
                wakeLock.Acquire((long)WakeLockTimeout.TotalMilliseconds);
            }

            _renewHandler?.PostDelayed(_renewAction!, (long)WakeLockRenewInterval.TotalMilliseconds);
        };

        _renewHandler.RemoveCallbacks(_renewAction);
        _renewHandler.PostDelayed(_renewAction, (long)WakeLockRenewInterval.TotalMilliseconds);
    }

    private void StopWakeLockRenewal()
    {
        if (_renewHandler != null && _renewAction != null)
        {
            _renewHandler.RemoveCallbacks(_renewAction);
        }
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
