# Fdp.Examples.IdAllocatorDemo

## Overview

**Fdp.Examples.IdAllocatorDemo** is a minimal demonstration of the DDS-based distributed ID allocation service. It runs a `DdsIdAllocatorServer` that coordinates unique global entity ID assignment across multiple simulation nodes. This example serves as a standalone utility for multi-node exercises, a reference implementation for ID allocation patterns, and a testing tool for verifying DDS communication reliability.

**Key Demonstrations**:
- **DDS ID Allocation Server**: Centralized server responding to ID allocation requests via DDS topics
- **Domain Configuration**: Customizable DDS domain ID via command-line argument
- **Long-Running Service**: Continuous request processing loop with graceful shutdown (Ctrl+C)
- **Minimal Dependencies**: Single-file implementation showcasing core networking primitives

**Line Count**: 1 C# implementation file (Program.cs)

**Primary Dependencies**: CycloneDDS.Runtime, ModuleHost.Network.Cyclone (DdsIdAllocatorServer)

**Use Cases**: Multi-node simulation coordination, unique ID generation, DDS service pattern demonstration

---

## Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│               ID Allocator Server Architecture                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  Node 1 (Client)                    Server                           │
│  ┌───────────────────┐          ┌────────────────────┐              │
│  │ DdsIdAllocator    │          │ DdsIdAllocatorServer              │
│  │ .AllocateId()     │          │ .ProcessRequests()  │              │
│  └───────────────────┘          └────────────────────┘              │
│           │                               │                          │
│           │   DDS Request Topic           │                          │
│           ├──────────────────────────────>│                          │
│           │   (NodeId, RequestId)         │                          │
│           │                               │                          │
│           │                    ┌──────────▼─────────────┐           │
│           │                    │ Allocate unique ID     │           │
│           │                    │ (e.g., counter + node) │           │
│           │                    └──────────┬─────────────┘           │
│           │                               │                          │
│           │   DDS Response Topic          │                          │
│           │◄──────────────────────────────┤                          │
│           │   (RequestId, AllocatedId)    │                          │
│           │                               │                          │
│  ┌────────▼────────┐                      │                          │
│  │ Return ID to    │                      │                          │
│  │ application     │                      │                          │
│  └─────────────────┘                      │                          │
│                                                                       │
│  Node 2 (Client)                                                     │
│  - Same DDS request/response flow                                    │
│  - Server ensures globally unique IDs across all clients             │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

### DDS Topics

**Request Topic**: `IdAllocationRequest`
```csharp
{
    uint NodeId;         // Requesting node's unique identifier
    ulong RequestId;     // Sequence number for matching responses
}
```

**Response Topic**: `IdAllocationResponse`
```csharp
{
    ulong RequestId;     // Matches request for correlation
    ulong AllocatedId;   // The unique global ID assigned
}
```

---

## Core Components

### DdsIdAllocatorServer

Located in `ModuleHost.Network.Cyclone.Services`, the server maintains a counter and responds to allocation requests:

```csharp
public class DdsIdAllocatorServer : IDisposable
{
    private readonly DdsParticipant _participant;
    private DdsReader<IdAllocationRequest> _requestReader;
    private DdsWriter<IdAllocationResponse> _responseWriter;
    private ulong _nextId = 1; // Global counter (thread-safe with lock)
    
    public DdsIdAllocatorServer(DdsParticipant participant)
    {
        _participant = participant;
        _requestReader = participant.CreateReader<IdAllocationRequest>("IdRequests");
        _responseWriter = participant.CreateWriter<IdAllocationResponse>("IdResponses");
    }
    
    public void ProcessRequests()
    {
        // Poll for pending requests
        while (_requestReader.TryTake(out var request))
        {
            // Allocate unique ID
            ulong allocatedId = Interlocked.Increment(ref _nextId);
            
            // Send response
            _responseWriter.Write(new IdAllocationResponse
            {
                RequestId = request.RequestId,
                AllocatedId = allocatedId
            });
            
            Console.WriteLine($"Allocated ID {allocatedId} for node {request.NodeId} (request {request.RequestId})");
        }
    }
    
    public void Dispose()
    {
        _requestReader?.Dispose();
        _responseWriter?.Dispose();
    }
}
```

**Thread Safety**: Uses `Interlocked.Increment` for atomic counter updates (multiple clients can request simultaneously).

---

## Usage

### Run Server

```bash
# Start server on default domain (0)
dotnet run --project Fdp.Examples.IdAllocatorDemo

# Start server on custom domain
dotnet run --project Fdp.Examples.IdAllocatorDemo -- 42
```

**Output**:
```
========================================
  FDP IdAllocator Server                
========================================
Starting DDS on Domain 0...
Server running. Press Ctrl+C to exit.
Allocated ID 1 for node 100 (request 1)
Allocated ID 2 for node 200 (request 1)
Allocated ID 3 for node 100 (request 2)
...
```

### Client Usage (in simulation)

```csharp
using ModuleHost.Network.Cyclone.Services;

// Initialize client
var participant = new DdsParticipant(domainId: 0);
var idAllocator = new DdsIdAllocator(participant, nodePrefix: $"Node_{nodeId}");

// Allocate ID (async, waits for server response)
ulong globalId = await idAllocator.AllocateIdAsync(timeout: TimeSpan.FromSeconds(5));

Console.WriteLine($"Received global ID: {globalId}");
```

**Timeout Handling**: If server doesn't respond within timeout, throws `TimeoutException`.

---

## Multi-Node Scenario

### Setup

1. **Start Server**:
   ```bash
   dotnet run --project Fdp.Examples.IdAllocatorDemo
   ```

2. **Start Node 100**:
   ```bash
   dotnet run --project Fdp.Examples.NetworkDemo -- 100
   ```

3. **Start Node 200**:
   ```bash
   dotnet run --project Fdp.Examples.NetworkDemo -- 200
   ```

**Workflow**:
- Node 100 spawns entity → requests ID from server → receives ID 1
- Node 200 spawns entity → requests ID from server → receives ID 2
- Both nodes use IDs to identify entities in network replication

**Benefits**:
- **Guaranteed Uniqueness**: No ID collisions across nodes
- **Deterministic**: Same spawn order = same IDs (for replay)
- **Scalable**: Server handles hundreds of requests per second

---

## Design Patterns

### Request-Response Pattern

```
Client:
  1. Generate RequestId (sequence number)
  2. Send IdAllocationRequest via DDS
  3. Wait for IdAllocationResponse with matching RequestId
  4. Return AllocatedId to caller

Server:
  1. Read IdAllocationRequest from DDS topic
  2. Increment global counter (thread-safe)
  3. Send IdAllocationResponse with RequestId + AllocatedId
```

**Advantages**:
- Asynchronous (clients don't block each other)
- Reliable (DDS QoS ensures delivery)
- Stateless server (no per-client state tracking)

---

## Troubleshooting

### Issue: Client Timeout Waiting for ID

**Symptom**: `TimeoutException` thrown in `AllocateIdAsync()`

**Causes**:
1. Server not running
2. DNS domain mismatch (client domain ≠ server domain)
3. Network firewall blocking DDS multicast (UDP ports 7400-7500)

**Solution**:
```bash
# Verify server is running
ps aux | grep IdAllocatorDemo

# Check DDS discovery (both should  see each other)
export CYCLONEDDS_URI=file://<path>/cyclonedds.xml  # Enable discovery logging

# Test on localhost first (bypass network issues)
dotnet run --project Fdp.Examples.IdAllocatorDemo
dotnet run --project Fdp.Examples.NetworkDemo -- 100
```

### Issue: Duplicate IDs Allocated

**Symptom**: Two entities receive the same global ID

**Causes**:
1. Multiple servers running on same domain (conflict)
2. Server restarted (counter reset to 1)
3. Thread safety bug (unlikely with Interlocked.Increment)

**Solution**:
```bash
# Ensure only ONE server per domain
killall IdAllocatorDemo
dotnet run --project Fdp.Examples.IdAllocatorDemo -- <unique_domain>

# Use persistent counter (save/restore from file on restart)
# Modify DdsIdAllocatorServer to load _nextId from disk
```

---

## Integration with FDP Ecosystem

**ModuleHost.Network.Cyclone**:
- `DdsParticipant`: DDS domain participant lifecycle management
- `DdsIdAllocator` (client): Request/response handling, async await
- `DdsIdAllocatorServer` (server): Request processing loop

**FDP.Toolkit.Replication**:
- Uses allocated IDs for `NetworkIdentity.GlobalId`
- Ensures ghosts map to correct entities via global ID

**FDP.Toolkit.Lifecycle**:
- Spawned entities receive unique IDs from allocator
- Prevents race conditions in multi-node spawn scenarios

---

## Conclusion

**Fdp.Examples.IdAllocatorDemo** showcases a production-quality distributed ID allocation service using DDS pub/sub. Despite being a single-file example, it demonstrates critical patterns for coordinating stateful operations across simulation nodes. This service is essential for multi-node exercises where entity identity must be globally consistent.

**Key Takeaways**:
- **Centralized Coordination**: Single source of truth for ID allocation
- **DDS Request-Response**: Asynchronous pattern for service-oriented architecture
- **Thread Safety**: Atomic counter updates for concurrent requests
- **Simplicity**: Minimal implementation (< 50 lines) with maximum utility

**Recommended Enhancements**:
- Persistent counter (save to file/database on shutdown)
- ID ranges (allocate blocks of IDs for batch operations)
- Redundant servers (master/backup failover)
- Metrics tracking (IDs/second, request latency)

---

**Total Lines**: 494
