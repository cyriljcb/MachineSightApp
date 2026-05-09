using System;

namespace MachineSightApp.Models;

public class MachineData
{
    public double Temperature {get;set;}

    public double Pressure {get;set;}
    public double Vibration {get;set;}
    public double Speed {get;set;}

    public double CurrentA {get; set;}
    public int ProductionCount {get;set;}
    public double CycleTimeMs {get;set;}

    public DateTime TimeStamp {get;set;}

    public bool AlarmTemp {get;set;}
    public bool AlarmVibration {get;set;}
    public bool AlarmPressure {get;set;}
    public bool AlarmEmergency {get;set;}

}
