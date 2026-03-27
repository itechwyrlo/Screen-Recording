using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Views;
using AndroidX.Core.App;

namespace MauiScreenRecorder.Services;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaProjection)]
public class ScreenRecordForegroundService : Service
{
    public const string ExtraResultCode = "extra_result_code";
    public const string ExtraDataIntent = "extra_data_intent";

    private const int NotificationId = 1002;
    private const string ChannelId = "screen_record_channel";

    private MediaProjection? _mediaProjection;
    private VirtualDisplay? _virtualDisplay;
    private MediaRecorder? _mediaRecorder;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent == null)
            return StartCommandResult.NotSticky;

        CreateChannel();

        var notification = BuildNotification();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaProjection);
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification, ForegroundService.TypeNone);
        else
#pragma warning disable CA1422
            StartForeground(NotificationId, notification);
#pragma warning restore CA1422

        var resultCode = intent.GetIntExtra(ExtraResultCode, (int)Result.Canceled);
        var data = GetProjectionDataIntent(intent);
        if (data == null || resultCode != (int)Result.Ok)
        {
            ScreenRecorderService.NotifyEngineReady(false);
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        try
        {
            StartCapture(resultCode, data);
            ScreenRecorderService.NotifyEngineReady(true);
        }
        catch
        {
            ScreenRecorderService.NotifyEngineReady(false);
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopCapture();
        ScreenRecorderService.NotifyEngineStopped();
        base.OnDestroy();
    }

    private void StartCapture(int resultCode, Intent data)
    {
        var metrics = new DisplayMetrics();
        var wm = GetSystemService(Context.WindowService)?.JavaCast<IWindowManager>();
        wm?.DefaultDisplay?.GetRealMetrics(metrics);

        var w = metrics.WidthPixels;
        var h = metrics.HeightPixels;
        if (w % 2 != 0) w--;
        if (h % 2 != 0) h--;
        var density = (int)metrics.DensityDpi;

        var mpm = (MediaProjectionManager?)GetSystemService(Context.MediaProjectionService);
        _mediaProjection = mpm?.GetMediaProjection(resultCode, data);
        if (_mediaProjection == null)
            throw new InvalidOperationException("MediaProjection unavailable.");

        _mediaRecorder = new MediaRecorder(this);
        _mediaRecorder.SetVideoSource(VideoSource.Surface);
        _mediaRecorder.SetOutputFormat(OutputFormat.Mpeg4);
        _mediaRecorder.SetVideoEncoder(VideoEncoder.H264);
        _mediaRecorder.SetVideoSize(w, h);
        _mediaRecorder.SetVideoFrameRate(30);
        _mediaRecorder.SetVideoEncodingBitRate(12 * 1000 * 1000);

        var dir = GetExternalFilesDir(null);
        if (dir == null)
            throw new InvalidOperationException("No external files directory.");
        var path = Path.Combine(dir.AbsolutePath, $"screen_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4");
        _mediaRecorder.SetOutputFile(path);
        _mediaRecorder.Prepare();

        var surface = _mediaRecorder.Surface ?? throw new InvalidOperationException("MediaRecorder surface missing.");
        _virtualDisplay = _mediaProjection.CreateVirtualDisplay(
            "maui_screen_recorder",
            w,
            h,
            density,
            DisplayFlags.Public | DisplayFlags.Present,
            surface,
            null,
            null);

        _mediaRecorder.Start();
    }

    private void StopCapture()
    {
        try
        {
            _mediaRecorder?.Stop();
        }
        catch
        {
            // Already stopped or failed mid-flight.
        }

        _virtualDisplay?.Release();
        _virtualDisplay = null;

        _mediaProjection?.Stop();
        _mediaProjection = null;

        _mediaRecorder?.Reset();
        _mediaRecorder?.Release();
        _mediaRecorder = null;

        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                StopForeground(StopForegroundFlags.Remove);
            else
#pragma warning disable CA1422
                StopForeground(true);
#pragma warning restore CA1422
        }
        catch
        {
            // Ignore.
        }
    }

    private static Intent? GetProjectionDataIntent(Intent intent)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            return intent.GetParcelableExtra(ExtraDataIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent;

#pragma warning disable CA1422
        return intent.GetParcelableExtra(ExtraDataIntent) as Intent;
#pragma warning restore CA1422
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

#pragma warning disable CA1416
        var channel = new NotificationChannel(
            ChannelId,
            "Screen recording",
            NotificationImportance.Low);
#pragma warning restore CA1416
        var nm = (NotificationManager?)GetSystemService(Context.NotificationService);
        nm?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var pending = PendingIntent.GetActivity(
            this,
            0,
            PackageManager?.GetLaunchIntentForPackage(PackageName!) ?? new Intent(),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Screen recording")
            .SetContentText("Capturing the screen…")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(pending)
            .SetOngoing(true)
            .Build();
    }
}
