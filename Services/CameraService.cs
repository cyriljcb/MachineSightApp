using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MachineSightApp.Interfaces;

namespace MachineSightApp.Services;

public class CameraService : ICameraService
{
    private HttpClient _httpClient;
    public event Action<byte[]>? FrameReceived;
    CancellationTokenSource _cts;

    public CameraService()
    {
        _httpClient = new HttpClient();    
    }

    public async Task StartAsync(string url)
    {
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        try
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            using var stream = await response.Content.ReadAsStreamAsync();

            while (!token.IsCancellationRequested)
            {
                int contentLength = -1;
                while (true)
                {
                    string? line = await ReadLineAsync(stream,token);
                    if(line==null) break;
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        contentLength = int.Parse(line.Split(":")[1].Trim());
                    if (line == "" && contentLength > 0) break;
                }
                if (contentLength <= 0) continue;
                byte[] buffer = new byte[contentLength];
                await stream.ReadExactlyAsync(buffer, 0, contentLength, token);
                FrameReceived?.Invoke(buffer);
            }
        }
        catch(OperationCanceledException ex){}
        catch(Exception ex)
        {
            Console.WriteLine($"[CAM] StartAsync: {ex.Message}");
            await Task.Delay(2000);
        }
    }

    private async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var buffer = new System.Text.StringBuilder();
        byte[] oneByte = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(oneByte, token);
            if (read == 0) return null;
            char c = (char)oneByte[0];
            if (c == '\n') return buffer.ToString().TrimEnd('\r');
            buffer.Append(c);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
