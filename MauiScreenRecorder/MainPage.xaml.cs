using MauiScreenRecorder.Services;

namespace MauiScreenRecorder;

public partial class MainPage : ContentPage
{
    private readonly IScreenRecorder _recorder;

    public MainPage()
    {
        InitializeComponent();
        _recorder = new ScreenRecorderService();
    }

    private async void OnRecordClicked(object sender, EventArgs e)
    {
        if (!_recorder.IsRecording)
        {
            await _recorder.StartAsync();
            if (_recorder.IsRecording)
                RecordButton.Text = "Stop Recording";
            else
                await DisplayAlert("Screen capture", "Permission was not granted or recording failed to start.", "OK");
        }
        else
        {
            await _recorder.StopAsync();
            RecordButton.Text = "Start Recording";
        }
    }
}