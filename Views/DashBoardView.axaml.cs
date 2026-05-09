using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MachineSightApp.ViewModels;

namespace MachineSightApp.Views;

public partial class DashBoardView : UserControl
{
    public DashBoardView()
    {
        InitializeComponent();
    }
    private void OnTempCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is DashBoardViewModel vm)
            vm.UpdateCanvasWidth(e.NewSize.Width);
    }

    private void OnVibCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is DashBoardViewModel vm)
            vm.UpdateCanvasWidth(e.NewSize.Width);
    }
}