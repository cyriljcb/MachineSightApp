using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MachineSightApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    private DashBoardViewModel _dashBoardViewModel;
    private CameraViewModel _cameraViewModel;

    public MainWindowViewModel(DashBoardViewModel dashBoardViewModel, CameraViewModel cameraViewModel)
    {
        _dashBoardViewModel = dashBoardViewModel;
        CurrentViewModel = dashBoardViewModel;
        _cameraViewModel = cameraViewModel;
    }

    [RelayCommand]
    private void NavigateToDashBoard()
    {
        CurrentViewModel = _dashBoardViewModel;
    }
    [RelayCommand]
    private void NavigateToCamera()
    {
        CurrentViewModel=_cameraViewModel;
    }
    [RelayCommand]
    private void Stop()
    {
        Environment.Exit(0);
    }
}
