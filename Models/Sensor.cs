using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore.Defaults;

namespace MachineSightApp.Models;

public partial class Sensor : ObservableObject
{
    public string Name {get; init;} = "";
    public double? WarnThreshold {get; init;}
    public double? DangerThreshold {get; init;}
    public double Maximum {get; init;}
    public ObservableCollection<ObservableValue> History {get;} = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private double _value;
    private const int MaxHistory = 60;
    public SensorState State => this switch
    {
        _ when DangerThreshold.HasValue && Value >= DangerThreshold => SensorState.Danger,
        _ when WarnThreshold.HasValue && Value >= WarnThreshold     => SensorState.Warn,
        _                                                           => SensorState.Ok,
    };
    public void AddHistory(double value)
    {
        History.Add(new ObservableValue(value));
        if (History.Count > MaxHistory)
            History.RemoveAt(0);
    }
}
public class PressureSensor : Sensor
{
    public double LowThreshold { get; init; }

    public new SensorState State =>
        (Value < LowThreshold || Value > WarnThreshold)
            ? SensorState.Warn
            : SensorState.Ok;
}
