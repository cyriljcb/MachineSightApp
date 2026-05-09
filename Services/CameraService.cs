using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MachineSightApp.Interfaces;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Wrap;

namespace MachineSightApp.Services;

public class CameraService : ICameraService
{
    private HttpClient _httpClient;
    private string? _lastUrl;
    public event Action<byte[]>? FrameReceived;
    private CancellationTokenSource _cts = new();

    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly AsyncPolicyWrap _policy;

    public CameraService()
    {
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, delay, attempt, _) =>
                    Console.WriteLine($"[CAM] Retry {attempt}/5 dans {delay.TotalSeconds:F0}s — {ex.Message}"));

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(20),
                onBreak: (ex, duration) =>
                    Console.WriteLine($"[CAM] Circuit OUVERT — flux caméra indisponible, pause {duration.TotalSeconds}s"),
                onReset: () =>
                    Console.WriteLine("[CAM] Circuit FERMÉ — flux caméra rétabli"),
                onHalfOpen: () =>
                    Console.WriteLine("[CAM] Circuit SEMI-OUVERT — test reconnexion caméra..."));

        _policy = _circuitBreakerPolicy.WrapAsync(_retryPolicy);
    }

    public async Task StartAsync(string url)
    {
        _lastUrl = url;
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        // Boucle externe — ne s'arrête jamais sauf annulation
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _policy.ExecuteAsync(async () =>
                {
                    Console.WriteLine($"[CAM] Connexion au flux {url}...");

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response = await _httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, token);
                    using var stream = await response.Content.ReadAsStreamAsync(token);

                    Console.WriteLine("[CAM] Flux connecté — réception des frames");

                    // Boucle interne — lit les frames tant que le flux est actif
                    while (!token.IsCancellationRequested)
                    {
                        int contentLength = -1;
                        while (true)
                        {
                            string? line = await ReadLineAsync(stream, token);
                            if (line == null)
                                throw new EndOfStreamException("Flux MJPEG interrompu");

                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                contentLength = int.Parse(line.Split(':')[1].Trim());

                            if (line == "" && contentLength > 0) break;
                        }

                        if (contentLength <= 0) continue;

                        byte[] buffer = new byte[contentLength];
                        await stream.ReadExactlyAsync(buffer, 0, contentLength, token);
                        FrameReceived?.Invoke(buffer);
                    }
                });
            }
            catch (BrokenCircuitException)
            {
                Console.WriteLine("[CAM] Circuit ouvert — pause 5s avant retry");
                await Task.Delay(5000, token);
                continue;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Retries épuisés — on continue la boucle externe sans throw
                Console.WriteLine($"[CAM] Echec après retries: {ex.Message}");
                await Task.Delay(5000, token);
                continue;
            }
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
        _cts.Cancel();
        _cts.Dispose();
    }
}