using System;
using System.Threading.Tasks;

namespace MachineSightApp.Interfaces;

public interface ICameraService
{
    event Action<byte[]>? FrameReceived;
    Task StartAsync(string url);
    void Stop();
}
