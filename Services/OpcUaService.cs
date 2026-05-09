using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Vulkan;
using MachineSightApp.Interfaces;
using MachineSightApp.Models;
using Opc.Ua;
using Opc.Ua.Client;

namespace MachineSightApp.Services;

public class OpcUaService : IOpcUaService
{
    private ISession? _session;
    public event Action<MachineData>? DataReceived;
    private CancellationTokenSource _cts = new();

   public async Task ConnectAsync(string url)
    {
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
                ).CreateForRSA().AddToStore(
                    certId.StoreType,
                    certId.StorePath
                );
                Console.WriteLine("[OPC UA] Certificat généré.");
            }

            config.CertificateValidator.CertificateValidation += (s, e) =>
            {
                e.Accept = true;
            };

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
        if(_session != null)
        {
            await _session.CloseAsync();
            _session.Dispose();
        }
    }

    public async Task StartPollingAsync()
    {
        CancellationToken token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if(_session == null || !_session.Connected)
                {
                    await Task.Delay(1000,token);
                    continue;
                }
                T Read<T>(uint identifier)
                {
                    var nodeId = new NodeId(identifier, 2);
                    var value = _session.ReadValue(nodeId);
                    return (T)Convert.ChangeType(value.Value, typeof(T));
                }

                var data = new MachineData
                {
                    Temperature     = Read<double>(3),
                    Pressure        = Read<double>(4),
                    Speed        = Read<double>(5),
                    Vibration       = Read<double>(6),
                    CurrentA        = Read<double>(7),
                    ProductionCount = Read<int>(8),
                    CycleTimeMs     = Read<double>(9),
                    AlarmTemp       = Read<bool>(14),
                    AlarmPressure   = Read<bool>(15),
                    AlarmVibration  = Read<bool>(16),
                    AlarmEmergency = Read<bool>(17),
                    TimeStamp       = DateTime.Now
                    
                };
                DataReceived?.Invoke(data);
            }
            catch(OperationCanceledException){}
            catch (Exception ex)
            {
                Console.WriteLine($"[OPC UA] Erreur polling: {ex.Message}");
                await Task.Delay(2000, token);
            }

            await Task.Delay(200, token);
        }
    }

    public async Task WriteCommandAsync(uint nodeId, bool value)
    {
         var nodesToWrite = new WriteValueCollection
        {
            new WriteValue
            {
                NodeId      = new NodeId(nodeId, 2),
                AttributeId = Attributes.Value,
                Value       = new DataValue(new Variant(value))
            }
        };

        await _session.WriteAsync(
            null,           // RequestHeader — null = défaut
            nodesToWrite,
            _cts.Token
        );
    }
}
