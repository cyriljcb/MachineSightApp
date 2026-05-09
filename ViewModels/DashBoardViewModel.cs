using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MachineSightApp.Interfaces;
using MachineSightApp.Models;
using MachineSightApp.Services;

namespace MachineSightApp.ViewModels;

public partial class DashBoardViewModel : ViewModelBase
{
    private double _canvasWidth  = 600;
    private const double CanvasHeight = 100;

    public Sensor Temperature { get; } = new()
    {
        Name            = "Température",
        WarnThreshold   = 85,
        DangerThreshold = 95,
        Maximum         = 120
    };
    public PressureSensor Pression { get; } = new()
    {
        Name          = "Pression",
        WarnThreshold = 4.5,
        Maximum       = 6
    };
    public Sensor Vibration { get; } = new()
    {
        Name            = "Vibration",
        WarnThreshold   = 6.0,
        DangerThreshold = 10.0,
        Maximum         = 15
    };
    public Sensor Vitesse { get; } = new() { Name = "Vitesse", Maximum = 2000 };
    public Sensor Courant { get; } = new() { Name = "Courant", Maximum = 20  };

    private readonly Queue<double> _tempHistory = new(60);
    private readonly Queue<double> _vibHistory  = new(60);

    [ObservableProperty] private List<Point>? _temperaturePoints;
    [ObservableProperty] private List<Point>? _vibrationPoints;
    [ObservableProperty] private List<Point>? _tempThresholdLine;
    [ObservableProperty] private List<Point>? _vibrationThresholdLine;

    [ObservableProperty] private double   _cycleTimeMs;
    [ObservableProperty] private long     _productionCount;
    [ObservableProperty] private bool     _alarmTemp;
    [ObservableProperty] private bool     _alarmPressure;
    [ObservableProperty] private bool     _alarmVibration;
    [ObservableProperty] private bool     _alarmEmergency;
    [ObservableProperty] private DateTime _timestamp;

    [ObservableProperty] private string  _connectionLabel = "Connexion...";
    [ObservableProperty] private string  _connectionColor = "Gray";
    [ObservableProperty] private bool    _isConnected     = false;

    private readonly IOpcUaService _client;

    public DashBoardViewModel(IOpcUaService opcUaService)
    {
        _client = opcUaService;
        _client.DataReceived += OnDataReceived;
        _client.ConnectionStatusChanged += OnConnectionStatusChanged;
        for (int i = 0; i < 60; i++)
        {
            _tempHistory.Enqueue(0);
            _vibHistory.Enqueue(0);
        }

        RefreshPoints();
    }

    private void OnDataReceived(MachineData data)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Temperature.Value = data.Temperature;
            Pression.Value    = data.Pressure;
            Vitesse.Value     = data.Speed;
            Vibration.Value   = data.Vibration;
            Courant.Value     = data.CurrentA;
            CycleTimeMs       = data.CycleTimeMs;
            ProductionCount   = data.ProductionCount;
            AlarmTemp         = data.AlarmTemp;
            AlarmPressure     = data.AlarmPressure;
            AlarmVibration    = data.AlarmVibration;
            AlarmEmergency    = data.AlarmEmergency;
            Timestamp         = data.TimeStamp;

            PushValue(_tempHistory, data.Temperature);
            PushValue(_vibHistory,  data.Vibration);

            RefreshPoints();
        });
    }

    public void UpdateCanvasWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - _canvasWidth) < 1) return;
        _canvasWidth = width;
        RefreshPoints();
    }

    private void RefreshPoints()
    {
        TemperaturePoints    = ComputePoints(_tempHistory, Temperature.Maximum);
        VibrationPoints      = ComputePoints(_vibHistory,  Vibration.Maximum);

        double tempY = ThresholdY(Temperature.DangerThreshold ?? 0, Temperature.Maximum);
        double vibY  = ThresholdY(Vibration.DangerThreshold   ?? 0, Vibration.Maximum);

        TempThresholdLine      = [ new Point(0, tempY), new Point(_canvasWidth, tempY) ];
        VibrationThresholdLine = [ new Point(0, vibY),  new Point(_canvasWidth, vibY)  ];
    }


    private static void PushValue(Queue<double> queue, double value)
    {
        if (queue.Count >= 60) queue.Dequeue();
        queue.Enqueue(value);
    }

    private List<Point> ComputePoints(Queue<double> queue, double maxValue)
    {
        var list   = queue.ToArray();
        var points = new List<Point>(list.Length);
        int count  = list.Length;

        for (int i = 0; i < count; i++)
        {
            double x = i * (_canvasWidth / Math.Max(count - 1, 1));
            double y = CanvasHeight - (list[i] / maxValue * CanvasHeight);
            points.Add(new Point(x, Math.Clamp(y, 0, CanvasHeight)));
        }

        return points;
    }

    private static double ThresholdY(double threshold, double maxValue) =>
        CanvasHeight - (threshold / maxValue * CanvasHeight);

    [RelayCommand] private async Task StartMachine()     => await _client.WriteCommandAsync(1031, true);
    [RelayCommand] private async Task StopMachine()      => await _client.WriteCommandAsync(1032, true);
    [RelayCommand] private async Task EmergencyMachine() => await _client.WriteCommandAsync(1033, true);
    [RelayCommand] private async Task ResetMachine()     => await _client.WriteCommandAsync(1034, true);
    [RelayCommand] private async Task InjectData()       => await _client.WriteCommandAsync(1035, true);

    private void OnConnectionStatusChanged(ConnectionStatus status)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            (ConnectionLabel, ConnectionColor, IsConnected) = status switch
            {
                ConnectionStatus.Connected    => ("En marche",          "Green",  true),
                ConnectionStatus.Connecting   => ("Connexion...",       "Gray",   false),
                ConnectionStatus.Retrying     => ("Reconnexion...",     "Orange", false),
                ConnectionStatus.Disconnected => ("Problème connexion", "Red",    false),
                _                             => ("Inconnu",            "Gray",   false)
            };
        });
    }
}