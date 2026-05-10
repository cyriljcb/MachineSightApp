# MachineSightApp — Client de supervision industrielle

Interface de supervision en temps réel pour le simulateur MachineSight.  
Développé en **C# / Avalonia UI** avec une architecture MVVM.  
Intègre **Polly** pour la résilience OPC UA (retry exponentiel + circuit breaker).

---

## Prérequis

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Le serveur MachineSight doit tourner sur le Raspberry Pi
- Le PC et le Pi doivent être sur le même réseau local

---

## Architecture

```
MachineSightApp/
  ├── Interfaces/
  │   ├── IOpcUaService.cs       Contrat du service OPC UA + enum ConnectionStatus
  │   └── ICameraService.cs      Contrat du service caméra
  ├── Services/
  │   ├── OpcUaService.cs        Connexion OPC UA + Polly retry/circuit breaker
  │   └── CameraService.cs       Réception flux MJPEG HTTP + Polly retry
  ├── ViewModels/
  │   ├── MainWindowViewModel    Navigation entre les vues
  │   ├── DashBoardViewModel     Données machine, alarmes, statut connexion
  │   └── CameraViewModel        Affichage flux vidéo
  ├── Views/                     Vues Avalonia (XAML)
  ├── Models/
  │   └── MachineData.cs         Modèle de données machine
  └── Converters/                Convertisseurs XAML (couleurs, opacité)

MachineSightApp.Tests/
  └── OpcUaServiceTests.cs       13 tests unitaires (Polly, retry, circuit breaker)
```

> **Diagramme d'architecture**  
> ![Architecture serveur](docs/ARchiClient.jpg)

---

## Installation et lancement

### 1. Cloner le repo

```bash
git clone <url-du-repo>
cd MachineSightApp
```

### 2. Configurer l'IP du Raspberry Pi

Dans `App.axaml.cs`, mettre à jour les URLs avec l'IP de ton Pi :

```csharp
opcUaService.SetUrl("opc.tcp://192.168.1.42:4840/machinesight/simulator/");
// ...
await cameraService.StartAsync("http://192.168.1.42:5000/stream");
```

### 3. Lancer l'application

```bash
dotnet run
```

---

## Résilience OPC UA — Polly

Le client implémente deux policies Polly combinées en pipeline :

### Retry exponentiel

En cas d'erreur de lecture OPC UA, le client retente automatiquement  
avec un délai exponentiel avant chaque tentative :

| Tentative | Délai d'attente |
|-----------|----------------|
| Retry 1   | 2s |
| Retry 2   | 4s |
| Retry 3   | 8s |
| Retry 4   | 16s |
| Retry 5   | 32s |

### Circuit breaker

Si 3 exceptions consécutives se produisent, le circuit s'ouvre :  
le client arrête de tenter pendant **20 secondes** pour ne pas surcharger  
un serveur indisponible, puis passe en mode semi-ouvert pour tester la reconnexion.

### Reconnexion automatique

Quand la session OPC UA est perdue, `_ensureConnectedAsync()` recrée  
automatiquement une nouvelle session sans intervention de l'utilisateur.

### Indicateur visuel

L'interface reflète l'état de connexion en temps réel :

| État | Label | Couleur |
|------|-------|---------|
| Connexion initiale | Connexion... | Gris |
| Connecté | En marche | Vert |
| Retry en cours | Reconnexion... | Orange |
| Circuit ouvert | Problème connexion | Rouge |

Quand le client est déconnecté, les jauges et capteurs sont grisés (opacité 40%).

---

## Tester la résilience manuellement

### 1. Lancer le serveur Pi

```bash
python3 main.py
```

### 2. Lancer le client C#

```bash
dotnet run
```

### 3. Déclencher une déconnexion depuis Windows

```powershell
# Couper OPC UA pendant 15 secondes
curl -X POST "http://192.168.1.42:5000/debug/disconnect?seconds=15"
```

### 4. Observer les logs dans le terminal C#

```
[OPC UA] Status → Retrying
[Polly] Retry 1/5 dans 2s — [80AE0000] (BadConnectionClosed)
[Polly] Retry 2/5 dans 4s — [80AE0000] (BadConnectionClosed)
[OPC UA] Session perdue — tentative de reconnexion...
[OPC UA] Connecté à opc.tcp://192.168.1.42:4840/machinesight/simulator/
[OPC UA] Status → Connected
```

---

## Tests unitaires

```bash
dotnet test MachineSightApp.Tests\MachineSightApp.Tests.csproj --logger "console;verbosity=normal"
```

### Résultats attendus : 13/13 tests réussis

| Catégorie | Test | Description |
|-----------|------|-------------|
| Nominaux | `ConnectAsync_ShouldComplete_WhenServerAvailable` | Connexion sans erreur |
| Nominaux | `WriteCommandAsync_ShouldComplete_WhenConnected` | Écriture sans erreur |
| Nominaux | `DataReceived_ShouldFire_WhenInvoked` | Event de données déclenché |
| Nominaux | `ConnectionStatusChanged_ShouldFire_WhenInvoked` | Event de statut déclenché |
| Retry | `RetryPolicy_ShouldRetry_WhenOperationFails` | Retente N fois avant succès |
| Retry | `RetryPolicy_ShouldSucceed_OnFirstAttempt_WhenNoException` | Aucun retry si succès immédiat |
| Retry | `RetryPolicy_ShouldThrow_AfterAllRetriesExhausted` | Exception après épuisement des retries |
| Retry | `RetryPolicy_ShouldNotRetry_OnOperationCanceledException` | Pas de retry sur annulation |
| Circuit breaker | `CircuitBreaker_ShouldOpen_AfterThresholdExceeded` | Circuit s'ouvre après le seuil |
| Circuit breaker | `CircuitBreaker_ShouldReset_AfterBreakDuration` | Circuit se ferme après le délai |
| Circuit breaker | `CircuitBreaker_ShouldThrowBrokenCircuitException_WhenOpen` | BrokenCircuitException quand ouvert |
| Debug | `SetUrl_ShouldNotThrow_WhenCalled` | SetUrl sans exception |
| Debug | `DisconnectAsync_ShouldComplete_WithoutException` | Déconnexion propre |

---

## Dépendances principales

| Package | Version | Usage |
|---------|---------|-------|
| Avalonia | 11.2.1 | UI framework multiplateforme |
| CommunityToolkit.Mvvm | 8.4.1 | MVVM (ObservableProperty, RelayCommand) |
| OPCFoundation.NetStandard.Opc.Ua | 1.5.378.134 | Client OPC UA |
| Polly | 8.6.6 | Retry + circuit breaker |
| xUnit | 2.9.3 | Framework de tests |
| Moq | 4.20.72 | Mocking pour les tests |