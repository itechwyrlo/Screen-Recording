using Android.App;
using Android.Content;
using Android.Media.Projection;
using Android.OS;
using Microsoft.Maui.ApplicationModel;

namespace MauiScreenRecorder.Services;

public class ScreenRecorderService : IScreenRecorder
{
    public const int RequestMediaProjection = 57001;

    private static TaskCompletionSource<(Result ResultCode, Intent? Data)>? _projectionTcs;
    private static TaskCompletionSource<bool>? _engineReadyTcs;
    private static ScreenRecorderService? _activeInstance;

    public ScreenRecorderService() => _activeInstance = this;

    public bool IsRecording { get; private set; }

    public static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestMediaProjection)
            return;
        _projectionTcs?.TrySetResult((resultCode, data));
    }

    internal static void NotifyEngineReady(bool success) =>
        _engineReadyTcs?.TrySetResult(success);

    internal static void NotifyEngineStopped() =>
        _activeInstance?.MarkNotRecording();

    private void MarkNotRecording() => IsRecording = false;

    public async Task StartAsync()
    {
        if (IsRecording)
            return;

        var activity = Platform.CurrentActivity;
        if (activity == null)
            throw new InvalidOperationException("No foreground activity; cannot request screen capture.");

        _projectionTcs = new TaskCompletionSource<(Result, Intent?)>();

        var mpm = (MediaProjectionManager)activity.GetSystemService(Context.MediaProjectionService)!;
        activity.StartActivityForResult(mpm.CreateScreenCaptureIntent()!, RequestMediaProjection);

        var (resultCode, intentData) = await _projectionTcs.Task.ConfigureAwait(true);
        _projectionTcs = null;

        if (resultCode != Result.Ok || intentData == null)
            return;

        _engineReadyTcs = new TaskCompletionSource<bool>();

        var start = new Intent(activity, typeof(ScreenRecordForegroundService));
        start.PutExtra(ScreenRecordForegroundService.ExtraResultCode, (int)resultCode);
        start.PutExtra(ScreenRecordForegroundService.ExtraDataIntent, intentData);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            activity.StartForegroundService(start);
        else
            activity.StartService(start);

        var startupWait = _engineReadyTcs.Task;
        var completed = await Task.WhenAny(startupWait, Task.Delay(TimeSpan.FromSeconds(20))).ConfigureAwait(true);
        _engineReadyTcs = null;

        if (completed != startupWait || !await startupWait.ConfigureAwait(true))
        {
            activity.StopService(new Intent(activity, typeof(ScreenRecordForegroundService)));
            return;
        }

        IsRecording = true;
    }

    public Task StopAsync()
    {
        if (!IsRecording)
            return Task.CompletedTask;

        var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        ctx.StopService(new Intent(ctx, typeof(ScreenRecordForegroundService)));

        IsRecording = false;
        return Task.CompletedTask;
    }
}
