using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MachineSightApp.Interfaces;
using MachineSightApp.Models;
using Moq;
using Polly;
using Polly.CircuitBreaker;
using Xunit;

namespace MachineSightApp.Tests;

/// <summary>
/// Tests unitaires pour la résilience OPC UA via Polly.
/// On teste via un mock de IOpcUaService — pas de connexion réelle au Pi.
/// </summary>
public class OpcUaServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Crée un mock IOpcUaService qui émet des données factices.
    /// </summary>
    private static Mock<IOpcUaService> CreateConnectedMock()
    {
        var mock = new Mock<IOpcUaService>();
        mock.Setup(s => s.ConnectAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.StartPollingAsync())
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.WriteCommandAsync(It.IsAny<uint>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    /// <summary>
    /// Construit une policy Polly identique à celle de OpcUaService,
    /// pour tester le comportement retry/circuit breaker de manière isolée.
    /// </summary>
    private static (AsyncPolicy policy, List<int> retriedAttempts, List<ConnectionStatus> statuses)
        BuildPolicy(int exceptionsAllowedBeforeBreaking = 3, int retryCount = 5)
    {
        var retriedAttempts = new List<int>();
        var statuses = new List<ConnectionStatus>();

        var retryPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(1), // rapide pour les tests
                onRetry: (_, _, attempt, _) =>
                {
                    retriedAttempts.Add(attempt);
                    statuses.Add(ConnectionStatus.Retrying);
                });

        var circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: exceptionsAllowedBeforeBreaking,
                durationOfBreak: TimeSpan.FromMilliseconds(100),
                onBreak:    (_, _) => statuses.Add(ConnectionStatus.Disconnected),
                onReset:    ()     => statuses.Add(ConnectionStatus.Connected),
                onHalfOpen: ()     => statuses.Add(ConnectionStatus.Retrying));

        var policy = circuitBreakerPolicy.WrapAsync(retryPolicy);
        return (policy, retriedAttempts, statuses);
    }

    // ── Tests nominaux ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_ShouldComplete_WhenServerAvailable()
    {
        // Arrange
        var mock = CreateConnectedMock();

        // Act
        await mock.Object.ConnectAsync("opc.tcp://localhost:4840");

        // Assert
        mock.Verify(s => s.ConnectAsync("opc.tcp://localhost:4840"), Times.Once);
    }

    [Fact]
    public async Task WriteCommandAsync_ShouldComplete_WhenConnected()
    {
        // Arrange
        var mock = CreateConnectedMock();

        // Act
        await mock.Object.WriteCommandAsync(1031, true);

        // Assert
        mock.Verify(s => s.WriteCommandAsync(1031, true), Times.Once);
    }

    [Fact]
    public void DataReceived_ShouldFire_WhenInvoked()
    {
        // Arrange
        var mock = CreateConnectedMock();
        MachineData? received = null;
        mock.Object.DataReceived += data => received = data;

        var expected = new MachineData { Temperature = 72.5, Pressure = 3.2 };

        // Act — simule l'émission d'un event par le service
        mock.Raise(s => s.DataReceived += null, expected);

        // Assert
        received.Should().NotBeNull();
        received!.Temperature.Should().Be(72.5);
        received.Pressure.Should().Be(3.2);
    }

    [Fact]
    public void ConnectionStatusChanged_ShouldFire_WhenInvoked()
    {
        // Arrange
        var mock = CreateConnectedMock();
        var statuses = new List<ConnectionStatus>();
        mock.Object.ConnectionStatusChanged += s => statuses.Add(s);

        // Act
        mock.Raise(s => s.ConnectionStatusChanged += null, ConnectionStatus.Connected);
        mock.Raise(s => s.ConnectionStatusChanged += null, ConnectionStatus.Retrying);
        mock.Raise(s => s.ConnectionStatusChanged += null, ConnectionStatus.Disconnected);

        // Assert
        statuses.Should().ContainInOrder(
            ConnectionStatus.Connected,
            ConnectionStatus.Retrying,
            ConnectionStatus.Disconnected);
    }

    // ── Tests Polly — Retry ──────────────────────────────────────────────────

    [Fact]
    public async Task RetryPolicy_ShouldRetry_WhenOperationFails()
    {
        // Arrange
        var (policy, retriedAttempts, _) = BuildPolicy(retryCount: 3);
        int callCount = 0;

        // Act — échoue 2 fois puis réussit
        await policy.ExecuteAsync(async () =>
        {
            callCount++;
            if (callCount < 3)
                throw new Exception("Connexion échouée");
            await Task.CompletedTask;
        });

        // Assert
        callCount.Should().Be(3);
        retriedAttempts.Should().HaveCount(2);
        retriedAttempts.Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task RetryPolicy_ShouldSucceed_OnFirstAttempt_WhenNoException()
    {
        // Arrange
        var (policy, retriedAttempts, _) = BuildPolicy();
        int callCount = 0;

        // Act
        await policy.ExecuteAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
        });

        // Assert
        callCount.Should().Be(1);
        retriedAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_ShouldThrow_AfterAllRetriesExhausted()
    {
        // Arrange — circuit breaker avec seuil très haut pour ne pas interférer
        var (policy, retriedAttempts, _) = BuildPolicy(
            exceptionsAllowedBeforeBreaking: 99,
            retryCount: 3);

        // Act
        Func<Task> act = () => policy.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            throw new Exception("Serveur toujours indisponible");
        });

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Serveur toujours indisponible");
        retriedAttempts.Should().HaveCount(3);
    }

    [Fact]
    public async Task RetryPolicy_ShouldNotRetry_OnOperationCanceledException()
    {
        // Arrange
        var (policy, retriedAttempts, _) = BuildPolicy();

        // Act
        Func<Task> act = () => policy.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            throw new OperationCanceledException();
        });

        // Assert — OperationCanceledException ne doit pas déclencher de retry
        await act.Should().ThrowAsync<OperationCanceledException>();
        retriedAttempts.Should().BeEmpty();
    }

    // ── Tests Polly — Circuit Breaker ────────────────────────────────────────

    [Fact]
    public async Task CircuitBreaker_ShouldOpen_AfterThresholdExceeded()
    {
        // Arrange — circuit s'ouvre après 3 exceptions
        var (policy, _, statuses) = BuildPolicy(exceptionsAllowedBeforeBreaking: 3, retryCount: 0);

        // Act — provoquer 3 exceptions pour ouvrir le circuit
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await policy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    throw new Exception("Serveur down");
                });
            }
            catch { /* attendu */ }
        }

        // Assert — le circuit est maintenant ouvert
        Func<Task> act = () => policy.ExecuteAsync(() => Task.CompletedTask);
        await act.Should().ThrowAsync<BrokenCircuitException>();
        statuses.Should().Contain(ConnectionStatus.Disconnected);
    }

    [Fact]
    public async Task CircuitBreaker_ShouldReset_AfterBreakDuration()
    {
        // Arrange — durationOfBreak = 100ms dans BuildPolicy
        var (policy, _, statuses) = BuildPolicy(exceptionsAllowedBeforeBreaking: 3, retryCount: 0);

        // Ouvrir le circuit
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await policy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    throw new Exception("Serveur down");
                });
            }
            catch { /* attendu */ }
        }

        // Attendre la fin du break
        await Task.Delay(200);

        // Act — le circuit doit être en half-open puis se fermer si succès
        await policy.ExecuteAsync(() => Task.CompletedTask);

        // Assert
        statuses.Should().Contain(ConnectionStatus.Retrying);  // half-open
        statuses.Should().Contain(ConnectionStatus.Connected);  // reset
    }

    [Fact]
    public async Task CircuitBreaker_ShouldThrowBrokenCircuitException_WhenOpen()
    {
        // Arrange
        var (policy, _, _) = BuildPolicy(exceptionsAllowedBeforeBreaking: 2, retryCount: 0);

        // Ouvrir le circuit
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await policy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    throw new Exception("Down");
                });
            }
            catch { /* attendu */ }
        }

        // Act & Assert — appel immédiat doit lever BrokenCircuitException
        Func<Task> act = () => policy.ExecuteAsync(() => Task.CompletedTask);
        await act.Should().ThrowAsync<BrokenCircuitException>();
    }

    // ── Tests endpoint debug ─────────────────────────────────────────────────

    [Fact]
    public void SetUrl_ShouldNotThrow_WhenCalled()
    {
        // Arrange
        var mock = CreateConnectedMock();

        // Act & Assert
        mock.Object.Invoking(s => s.SetUrl("opc.tcp://192.168.1.1:4840"))
            .Should().NotThrow();
    }

    [Fact]
    public async Task DisconnectAsync_ShouldComplete_WithoutException()
    {
        // Arrange
        var mock = CreateConnectedMock();
        mock.Setup(s => s.DisconnectAsync()).Returns(Task.CompletedTask);

        // Act & Assert
        await mock.Object.Invoking(s => s.DisconnectAsync())
            .Should().NotThrowAsync();
    }
}