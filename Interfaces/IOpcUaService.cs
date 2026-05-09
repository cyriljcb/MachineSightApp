using System;
using System.Threading.Tasks;
using MachineSightApp.Models;

namespace MachineSightApp.Interfaces;

public interface IOpcUaService
{
    event Action<MachineData>? DataReceived;
    Task ConnectAsync(string url);
    Task StartPollingAsync();
    Task DisconnectAsync();
    Task WriteCommandAsync(uint nodeId, bool value);
    void SetUrl(string url);
}
