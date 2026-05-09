using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using Microsoft.Extensions.DependencyInjection;
using MachineSightApp.Views;
using MachineSightApp.ViewModels;
using MachineSightApp.Services;
using MachineSightApp.Interfaces;
using System.Threading.Tasks;

namespace MachineSightApp;

public partial class App : Application
{
    private IServiceProvider _serviceProvider;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _serviceProvider = ConfigureServices();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };
            var opcUaService = _serviceProvider.GetRequiredService<IOpcUaService>();
            var cameraService = _serviceProvider.GetRequiredService<ICameraService>();

            Task.Run(async () =>
            {
                try
                {
                    await opcUaService.ConnectAsync("opc.tcp://192.168.129.166:4840/machinesight/simulator/");
                    await opcUaService.StartPollingAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] OPC UA init failed: {ex.Message}");
                }
            });

            Task.Run(async () =>
            {
                
                try
                {
                    await cameraService.StartAsync("http://192.168.129.166:5000/stream");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] Camera init failed: {ex.Message}");
                }

            });
        }
        base.OnFrameworkInitializationCompleted();
    }


    public IServiceProvider ConfigureServices()
    {
        ServiceCollection services = new ServiceCollection();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<IOpcUaService,OpcUaService>();
        services.AddSingleton<ICameraService,CameraService>();

        services.AddTransient<DashBoardViewModel>();
        services.AddTransient<CameraViewModel>();
        return services.BuildServiceProvider();

    }

}