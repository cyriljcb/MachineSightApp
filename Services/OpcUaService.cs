using System;
using System.Threading;
using System.Threading.Tasks;
using MachineSightApp.Interfaces;
using MachineSightApp.Models;
using Opc.Ua;
using Opc.Ua.Client;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Wrap;

namespace MachineSightApp.Services;

public class OpcUaService : IOpcUaService
{
    private ISession? _session;
    private string? _lastUrl;
    public event Action<MachineData>? DataReceived;
    private CancellationTokenSource _cts = new();

    // ── Polly policies ──────────────────────────────────────────────────────
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly AsyncPolicyWrap _policy;

    public OpcUaService()
    {
        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s, 16s, 32s
                onRetry: (exception, delay, attempt, _) =>
                    Console.WriteLine(
                        $"[Polly] Retry {attempt}/5 dans {delay.TotalSeconds:F0}s — {exception.Message}"));

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(20),
                onBreak: (ex, duration) =>
                    Console.WriteLine(
                        $"[Polly] Circuit OUVERT — serveur OPC UA indisponible, pause {duration.TotalSeconds}s"),
                onReset: () =>
                    Console.WriteLine("[Polly] Circuit FERMÉ — connexion OPC UA rétablie"),
                onHalfOpen: () =>
                    Console.WriteLine("[Polly] Circuit SEMI-OUVERT — tentative de reconnexion..."));

        // Circuit breaker en outer, retry en inner
        _policy = _circuitBreakerPolicy.WrapAsync(_retryPolicy);
    }

    // ── Connexion ───────────────────────────────────────────────────────────

    public async Task ConnectAsync(string url)
    {
        _lastUrl = url; // mémorisé pour la reconnexion automatique
        try
        {
            var config = new ApplicationConfiguration()
            {
                ApplicationName = "MachineSight.Client",
                ApplicationUri  = Utils.ReplaceLocalhost("urn:localhost:MachineSight:Client"),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType   = CertificateStoreType.Directory,
                        StorePath   = "./pki/own",
                        SubjectName = "CN=MachineSight.Client, DC=localhost"
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "./pki/trusted"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "./pki/issuers"
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "./pki/rejected"
                    },
                    AutoAcceptUntrustedCertificates = true,
                    RejectSHA1SignedCertificates    = false,
                    MinimumCertificateKeySize       = 1024
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };

            await config.ValidateAsync(ApplicationType.Client);

            var certId = config.SecurityConfiguration.ApplicationCertificate;
            if (certId.Certificate == null)
            {
                Console.WriteLine("[OPC UA] Génération du certificat...");
                certId.Certificate = CertificateFactory.CreateCertificate(
                    config.ApplicationUri,
                    config.ApplicationName,
                    certId.SubjectName,
                    null
                ).CreateForRSA().AddToStore(certId.StoreType, certId.StorePath);
                Console.WriteLine("[OPC UA] Certificat généré.");
            }

            config.CertificateValidator.CertificateValidation += (s, e) => e.Accept = true;

            var endpoint = CoreClientUtils.SelectEndpoint(config, url, false);
            var endpointConfig = EndpointConfiguration.Create(config);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfig);

            _session = await Session.Create(
                config,
                configuredEndpoint,
                false,
                false,
                "MachineSight Session",
                60000,
                new UserIdentity(new AnonymousIdentityToken()),
                null
            );

            Console.WriteLine($"[OPC UA] Connecté à {url}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPC UA] Erreur de connexion: {ex.Message}");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        _cts.Cancel();
        if (_session != null)
        {
            await _session.CloseAsync();
            _session.Dispose();
        }
    }

    // ── Reconnexion automatique ─────────────────────────────────────────────

    private async Task _ensureConnectedAsync()
    {
        if (_session != null && _session.Connected)
            return;

        if (_lastUrl == null)
            throw new InvalidOperationException("ConnectAsync() n'a pas encore été appelé.");

        Console.WriteLine("[OPC UA] Session perdue — tentative de reconnexion...");
        await ConnectAsync(_lastUrl);
    }

    // ── Polling avec Polly ──────────────────────────────────────────────────

    public async Task StartPollingAsync()
    {
        CancellationToken token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var data = await _policy.ExecuteAsync(async () =>
                {
                    await _ensureConnectedAsync();

                    try
                    {
                        T Read<T>(uint identifier)
                        {
                            var nodeId = new NodeId(identifier, 2);
                            var value  = _session!.ReadValue(nodeId);
                            return (T)Convert.ChangeType(value.Value, typeof(T));
                        }

                        return new MachineData
                        {
                            Temperature     = Read<double>(1002),
                            Pressure        = Read<double>(1003),
                            Speed           = Read<double>(1004),
                            Vibration       = Read<double>(1005),
                            CurrentA        = Read<double>(1006),
                            ProductionCount = Read<int>   (1007),
                            CycleTimeMs     = Read<double>(1008),
                            AlarmTemp       = Read<bool>  (1021),
                            AlarmPressure   = Read<bool>  (1022),
                            AlarmVibration  = Read<bool>  (1023),
                            AlarmEmergency  = Read<bool>  (1024),
                            TimeStamp       = DateTime.Now
                        };
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("[DEBUG] Lecture échouée — session invalidée");
                        _session = null;
                        throw;
                    }
                });

                DataReceived?.Invoke(data);
            }
            catch (BrokenCircuitException)
            {
                await Task.Delay(5000, token);
                continue;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                
                Console.WriteLine($"[OPC UA] Echec après retries: {ex.Message}");
                await Task.Delay(5000, token);
                continue; 
            }

            await Task.Delay(200, token);
        }
    }

    // ── Écriture de commandes avec Polly ────────────────────────────────────

    public async Task WriteCommandAsync(uint nodeId, bool value)
    {
        await _policy.ExecuteAsync(async () =>
        {
            await _ensureConnectedAsync();

            var nodesToWrite = new WriteValueCollection
            {
                new WriteValue
                {
                    NodeId      = new NodeId(nodeId, 2),
                    AttributeId = Attributes.Value,
                    Value       = new DataValue(new Variant(value))
                }
            };

            await _session!.WriteAsync(null, nodesToWrite, _cts.Token);
        });
    }
    public void SetUrl(string url) => _lastUrl = url;
}