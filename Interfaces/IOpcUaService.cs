using System;
using System.Threading.Tasks;
using MachineSightApp.Models;

namespace MachineSightApp.Interfaces;

public enum ConnectionStatus { Connecting, Connected, Retrying, Disconnected }

public interface IOpcUaService
{
    event Action<MachineData>? DataReceived;
    event Action<ConnectionStatus>? ConnectionStatusChanged;
    void SetUrl(string url);
    Task ConnectAsync(string url);
    Task DisconnectAsync();
    Task StartPollingAsync();
    Task WriteCommandAsync(uint nodeId, bool value);
}
