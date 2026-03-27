namespace MauiScreenRecorder.Services;

public interface IScreenRecorder
{
    Task StartAsync();
    Task StopAsync();
    bool IsRecording { get; }
}