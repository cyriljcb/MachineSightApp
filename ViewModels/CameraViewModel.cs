using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MachineSightApp.Interfaces;

namespace MachineSightApp.ViewModels;

public partial class CameraViewModel : ViewModelBase
{
    [ObservableProperty]
    private Bitmap? _currentFrame;

    private readonly ICameraService _cameraService;

    public CameraViewModel(ICameraService cameraService)
    {
        _cameraService = cameraService;
        _cameraService.FrameReceived += onFrameReceived;
    }
    private void onFrameReceived(byte[] frameData)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                using var ms = new MemoryStream(frameData);
                CurrentFrame = new Bitmap(ms);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CAM VM] Erreur bitmap: {ex.Message}");
            }
        });
    }
}
