# Fdp.Examples.IdAllocatorDemo

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.IdAllocatorDemo/Fdp.Examples.IdAllocatorDemo.csproj` |
| **Output type** | Executable (`<OutputType>Exe</OutputType>`) |
| **Target framework** | net8.0 |
| **Date documented** | 2026-05-23 |

## README Validation

**Missing** — No README.md exists in the project folder. This document serves as the
primary reference.

---

## Executive Overview

`Fdp.Examples.IdAllocatorDemo` is a minimal **DDS-based distributed ID allocation server**.
It starts a `DdsIdAllocatorServer` that listens on a configurable DDS domain and responds
to ID allocation requests from client nodes, assigning each client a unique 64-bit integer
that remains valid for the lifetime of the DDS session.

This demo addresses a fundamental problem in distributed simulation: two nodes may try to
spawn entities simultaneously with the same local counter value. Without a shared
allocation authority, network ID collisions would cause entities on different nodes to
overwrite each other's state.

### Key learning objectives

1. **What `DdsIdAllocatorServer` does** — request/response over DDS for unique ID
   reservation without a centralized database.
2. **How to configure the DDS domain ID** via a command-line argument, demonstrating
   multi-domain isolation.
3. **Graceful cancellation** — `CancelKeyPress` with `CancellationTokenSource` for clean
   Ctrl+C shutdown.
4. **10 ms polling loop** — the server processes pending request batches every 10 ms;
   understanding this rate helps tune client timeouts.

---

## Architecture

### System Context

```
+--------------------------------------+
|         Simulation Network           |
|                                      |
|  +------------------+                |
|  | IdAllocatorDemo  |                |
|  | (this project)   |                |
|  |                  |                |
|  |  DdsParticipant  |                |
|  |  DdsIdAllocator  |                |
|  |  Server          |                |
|  |   .ProcessReqs() |                |
|  |      ^           |                |
|  +------|----------+                 |
|         |                            |
|   DDS domain (topic: IdAllocRequest, |
|              topic: IdAllocResponse) |
|         |                            |
|  +------|----------+  +----------+   |
|  | Brain Node      |  | Muscle   |   |
|  | (scenario)      |  | Node     |   |
|  | DdsIdAllocClient|  | DdsId    |   |
|  | .AllocateId()   |  | AllocClt |   |
|  +------------------+  +----------+  |
+--------------------------------------+
```

### Request/Response Flow

```
  Client Node                       IdAllocatorDemo
      |                                    |
      |  DDS Write: IdAllocRequestMsg      |
      |    { ClientNodeId=2, Count=10 }    |
      |----------------------------------->|
      |                                    | ProcessRequests()
      |                                    | Assigns range [1001..1010]
      |                                    | writes IdAllocResponseMsg
      |  DDS Read: IdAllocResponseMsg      |
      |    { ClientNodeId=2,               |
      |      BaseId=1001, Count=10 }       |
      |<-----------------------------------|
      |                                    |
      | Uses IDs 1001..1010 for entity     |
      | network IDs in DemoSpawnMsg        |
      |                                    |
```

### Process Lifecycle

```
+--------------------------------------------------------------+
|  Program.Main(args)                                          |
|                                                              |
|  1. Parse domainId from args[0] (default 0)                  |
|  2. new DdsParticipant(domainId)                             |
|  3. new DdsIdAllocatorServer(participant)                    |
|  4. Register CancelKeyPress -> cts.Cancel()                  |
|  5. Loop until cancellation:                                 |
|       server.ProcessRequests()                               |
|       await Task.Delay(10ms)                                 |
|  6. Participant + server disposed via using blocks           |
+--------------------------------------------------------------+
```

---

## Source Structure

```
FDP/Examples/Fdp.Examples.IdAllocatorDemo/
+-- Fdp.Examples.IdAllocatorDemo.csproj
+-- Program.cs
      namespace Fdp.Examples.IdAllocatorDemo
      class Program
        static async Task Main(string[] args)
```

The project is intentionally minimal — a single source file demonstrating the
`DdsIdAllocatorServer` API.

---

## Public API Reference

### `Program`

```csharp
namespace Fdp.Examples.IdAllocatorDemo
{
    class Program
    {
        static async Task Main(string[] args);
    }
}
```

#### `Main(string[] args)`

| Parameter | Description |
|---|---|
| `args[0]` (optional) | DDS domain ID as a `uint`. Defaults to `0` if omitted or unparseable. |

**Returns:** Exit code `0` on clean shutdown, non-zero on unhandled exception.

**Behavior:**

1. Parses `domainId` from `args[0]` if provided.
2. Creates `DdsParticipant` and `DdsIdAllocatorServer` inside `using` blocks.
3. Registers `Console.CancelKeyPress` to signal `CancellationTokenSource`.
4. Runs `server.ProcessRequests()` every 10 ms until cancellation is requested.
5. Prints `"Server stopped."` on exit.

### Types from `Fdp.Network.Cyclone.Services`

The following types are referenced but defined in `Fdp.Network.Cyclone`:

| Type | Description |
|---|---|
| `DdsIdAllocatorServer` | Listens for allocation requests; maintains the next-free ID counter; writes responses |
| `DdsParticipant` | CycloneDDS domain participant |

---

## Dependencies

### NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `CycloneDDS.NET` | 0.2.2 | DDS runtime used by `DdsIdAllocatorServer` internally |

### Project references

| Project | Purpose |
|---|---|
| `Fdp.ModuleHost` | Pulled in transitively; provides `FdpConfig` global settings |
| `Fdp.Network.Cyclone` | Provides `DdsIdAllocatorServer` and its DDS-based request/response protocol |

---

## Usage Examples

### Example 1 — Running the server on domain 0

```bash
cd FDP/Examples/Fdp.Examples.IdAllocatorDemo
dotnet run
# Output:
# ========================================
#   FDP IdAllocator Server
# ========================================
# Starting DDS on Domain 0...
# Server running. Press Ctrl+C to exit.
```

### Example 2 — Running the server on a custom domain

```bash
dotnet run -- 5
# Starting DDS on Domain 5...
# Server running. Press Ctrl+C to exit.
```

### Example 3 — Connecting a client to the server (in a scenario node)

```csharp
using CycloneDDS.Runtime;
using Fdp.Network.Cyclone.Services;

// Client-side: request a block of 10 unique IDs from domain 0
using var participant = new DdsParticipant(domainId: 0);
using var client = new DdsIdAllocatorClient(participant, localNodeId: 2);

// Blocks until server responds (or timeout)
long baseId = await client.AllocateAsync(count: 10, timeout: TimeSpan.FromSeconds(5));

// IDs baseId..baseId+9 are now reserved for this node
Console.WriteLine($"Allocated IDs: {baseId}..{baseId + 9}");

// Use these IDs when spawning entities:
for (int i = 0; i < 10; i++)
{
    spawnWriter.Write(new DemoSpawnMsg
    {
        NetworkId   = baseId + i,
        TkbType     = DemoTemplateIds.CommandTank,
        OwnerNodeId = 2,
        IsDestroyed = false,
    });
}
```

### Example 4 — Starting server and client together in integration tests

```csharp
[Test]
public async Task AllocatorServer_ReturnsUniqueIds_ToTwoClients()
{
    using var participant = new DdsParticipant(domainId: 99); // isolated domain
    using var server = new DdsIdAllocatorServer(participant);

    var clientTask1 = Task.Run(async () =>
    {
        using var p = new DdsParticipant(99);
        using var c = new DdsIdAllocatorClient(p, localNodeId: 1);
        return await c.AllocateAsync(count: 5, TimeSpan.FromSeconds(2));
    });

    var clientTask2 = Task.Run(async () =>
    {
        using var p = new DdsParticipant(99);
        using var c = new DdsIdAllocatorClient(p, localNodeId: 2);
        return await c.AllocateAsync(count: 5, TimeSpan.FromSeconds(2));
    });

    // Process requests concurrently
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            server.ProcessRequests();
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
        }
    });

    long base1 = await clientTask1;
    long base2 = await clientTask2;

    // Ranges must not overlap
    Assert.IsTrue(base1 + 5 <= base2 || base2 + 5 <= base1,
        "ID ranges from two clients must not overlap");
}
```

---

## Best Practices

### 1. Run the server before any client nodes

The `DdsIdAllocatorServer` uses DDS TransientLocal QoS for responses. If a client joins
before the server is alive, the client's request topic will have no readers. Start the
server first, or ensure the client implements retry logic with back-off.

### 2. Use isolated domain IDs in automated tests

Tests that instantiate the server should use a high domain ID (e.g., 90-127) to avoid
interfering with production simulations running on domain 0. See Example 4 above.

### 3. Call `ProcessRequests()` frequently

The server queues requests from DDS readers but does not process them in the background.
The `await Task.Delay(10)` loop in this demo gives ~10 ms maximum latency per allocation
request. For tighter latency requirements, reduce the delay.

### 4. Dispose resources in the correct order

`DdsIdAllocatorServer` must be disposed before `DdsParticipant`. The `using` block order
in `Main` guarantees this: server is declared after participant so it is disposed first.

### 5. Pre-allocate ID blocks, not single IDs

In production use, clients should request blocks of IDs (e.g., 100 at a time) rather
than requesting one per entity spawn. This reduces round-trip latency and prevents the
server from becoming a bottleneck when many entities spawn simultaneously.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Network.Cyclone` | Provides `DdsIdAllocatorServer` that this demo wraps |
| `Fdp.Examples.DDS` | Defines `DemoSpawnMsg` which uses `NetworkId` values allocated by this server |
| `Fdp.Examples.Scenarios/Network/DistributedTankScenario` | Uses ID allocation in a multi-node scenario |
| `Fdp.Examples.DER` | Client-side demo that would consume IDs from this server as entity keys |

---

## Architecture Deep Dive

### The Distributed ID Problem

In a single-process simulation each entity can be identified by its ECS `Entity` handle —
a process-local integer index + generation counter. This breaks down in multi-node simulations:

- Node A spawns entity at local index 7.
- Node B spawns a different entity, also at local index 7.
- Both nodes publish `DemoSpawnMsg { NetworkId = 7 }` to the DDS topic.
- All subscribers see two conflicting spawns with the same key — undefined behavior.

`DdsIdAllocatorServer` solves this by acting as the single authoritative source of unique
64-bit integers. Each node requests a block of IDs before spawning any entities; the server
guarantees no two blocks overlap.

### Protocol Overview

```
+-------+                          +--------+
| Client|                          | Server |
+-------+                          +--------+
    |                                   |
    | DDS Write: AllocRequest           |
    |   { ClientId=2, Count=10 }        |
    |---------------------------------->|
    |                                   | Assigns range [nextId .. nextId+9]
    |                                   | nextId += 10
    |                                   | DDS Write: AllocResponse
    |   DDS Read: AllocResponse         |   { ClientId=2, BaseId=nextId, Count=10 }
    |<----------------------------------|
    |                                   |
    | Uses IDs BaseId..BaseId+Count-1   |
    | for NetworkId in DemoSpawnMsg     |
    |                                   |
```

The server maintains a monotonically increasing counter. Requests from multiple clients are
serialized in `ProcessRequests()`. There is no persistent storage — IDs restart from 1 when
the server restarts. All nodes must restart together if the server restarts.

### Request Processing Loop

The server does not use background threads or async I/O internally. `ProcessRequests()` is
a synchronous call that reads all pending DDS samples from the request reader and writes
responses. The demo's `await Task.Delay(10)` loop calls it at ~100 Hz, which is sufficient
for low-traffic scenarios (< 1000 spawns/second).

For higher-throughput scenarios the delay can be reduced:

```csharp
// Ultra-low latency: process every millisecond
while (!cts.Token.IsCancellationRequested)
{
    server.ProcessRequests();
    await Task.Delay(1); // 1 ms poll interval
}
```

### Domain Isolation for Test Environments

Using a dedicated DDS domain ID for tests ensures that ID allocation servers and clients
from concurrent test runs do not interfere with each other:

```
Production simulation:  domain 0   (IDs start at 1)
Integration test suite: domain 90  (IDs start at 1, isolated)
Unit test A:            domain 91  (IDs start at 1, isolated)
Unit test B:            domain 92  (IDs start at 1, isolated)
```

CycloneDDS domains are fully isolated — participants on different domains cannot discover
or communicate with each other, even on the same machine.

### Fault Tolerance Considerations

The current demo has no fault tolerance:

| Failure | Effect |
|---|---|
| Server crashes and restarts | Counter resets to 1; risk of ID collision with previously allocated IDs |
| Network partition during request | Client times out; never receives IDs; spawning blocked |
| Server slow to respond | Client blocks in `AllocateAsync`; entity spawn latency increases |

For production use, consider:
- Persistent counter storage (file or database) for server restart recovery.
- Client-side retry with exponential back-off.
- Multiple server replicas with Raft-style leader election.

### Integration with ScenarioDirector

In `Fdp.Examples.UrbanCombat`, the `ScenarioDirector` uses a simple local counter
(`_nextNetId`) because the simulation runs in a single process. In a true multi-node
deployment, the `ScenarioDirector` would call `DdsIdAllocatorClient.AllocateAsync(count:
14)` during `SetupAmbushScenario()` to pre-allocate 14 IDs before spawning:

```csharp
// Multi-node version of SetupAmbushScenario:
public async Task SetupAmbushScenarioAsync(
    DdsIdAllocatorClient allocClient)
{
    // Pre-allocate IDs for all 14 entities in one round-trip
    long baseId = await allocClient.AllocateAsync(count: 14,
        timeout: TimeSpan.FromSeconds(5));

    long nextId = baseId;

    for (int i = 0; i < 5; i++)
    {
        SpawnEntityWithNetId(netId: nextId++,
            tkbTypeId: 1001, ...);
    }
    // ... etc.
}
```

### Minimum Required Infrastructure

To run a distributed FDP simulation, the IdAllocatorDemo must be the **first** process
started. The recommended startup order is:

```
1. fdp-demo-runner --scenario placeholder   <- starts DdsIdAllocatorServer
2. fdp-brain-node  --scenario distributedtank --role brain
3. fdp-muscle-node --scenario distributedtank --role muscle
```

In practice for single-machine demos, all three can be started in separate terminals within
a few hundred milliseconds of each other — the CycloneDDS discovery mechanism will connect
them automatically.

### Observing Allocations with ddstopic monitor

If CycloneDDS command-line tools are installed, you can observe allocations live:

```bash
# Watch allocation requests and responses on domain 0:
cyclonedds subscribe -d 0 IdAllocRequest
cyclonedds subscribe -d 0 IdAllocResponse
```

Each entity spawn will appear as an `IdAllocRequest` followed immediately by an
`IdAllocResponse` with the assigned `BaseId`.

### Single-File Project Design

The project deliberately contains exactly one source file. The purpose is to show that
launching a `DdsIdAllocatorServer` requires no boilerplate beyond:

1. Create `DdsParticipant`.
2. Create `DdsIdAllocatorServer`.
3. Call `server.ProcessRequests()` in a loop.

Any additional infrastructure (logging, health checks, metrics) is left to the consuming
application to add, keeping this demo focused on the minimum viable server.

### Relationship to FdpConfig

`Program.Main` does not call `Fdp.Core.FdpConfig.EnforceExplicitComponentIds = true`
or other global FDP configuration flags, because this server does not use the FDP ECS at
all. It is a pure DDS service. This is intentional and demonstrates that the ID allocator
subsystem can be run as a standalone infrastructure service without any simulation engine
dependency.

### Scalability Notes

The current `DdsIdAllocatorServer` design serves a single simulation session. For scenarios
with:

- **Many nodes** (> 100): The 10 ms poll interval may cause visible spawn latency. Consider
  reducing to 1 ms or using an event-driven DDS wait-set instead of polling.
- **Many simultaneous spawn bursts**: Pre-allocate larger blocks (100–1000 IDs per request)
  so nodes rarely need to ask the server.
- **Session restart without server restart**: If the server counter does not persist, all
  nodes must restart together. Add file-based persistence if partial restarts are required.
