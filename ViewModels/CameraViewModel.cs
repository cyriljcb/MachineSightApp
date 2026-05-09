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
    private void onFrameReceived(byte[] FrameReceived)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var ms = new MemoryStream(FrameReceived);
            CurrentFrame = new Bitmap(ms);
        }); 
    }
}
