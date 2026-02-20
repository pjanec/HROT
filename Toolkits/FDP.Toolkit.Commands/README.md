# FDP.Toolkit.Commands

A generic RPC-over-DDS toolkit for strongly-typed Request/Response (Ack) patterns, using CycloneDDS.

## Features

- **Generic Client**: `DdsCommandClient<TRequest, TAck>` handles the intricacies of waiting for a matching response.
- **Correlation**: Uses a user-provided extractor (e.g. `req => req.RequestId`) to map requests to responses.
- **Timeout Support**: `SendAsync` accepts a timeout in milliseconds.
- **Async API**: Fully awaitable `Task<TAck>` implementation.

## Usage

### 1. Define Messages

Ensure your Request and Ack structs have a unique ID field (usually `Guid RequestId`).

```csharp
[DdsTopic("CreateEntityRequest")]
public struct CreateEntityRequest
{
    [DdsKey]
    public Guid RequestId;
    public int EntityId;
    // ...
}

[DdsTopic("CreateEntityAck")]
public struct CreateEntityAck
{
    [DdsKey]
    public Guid RequestId;
    public int ResultCode;
}
```

### 2. Create the Client

```csharp
using FDP.Toolkit.Commands;

// ... inside your application setup ...
var client = new DdsCommandClient<CreateEntityRequest, CreateEntityAck>(
    participant,           // CycloneDDS Participant
    "CreateEntityRequest", // Topic Name
    "CreateEntityAck",     // Topic Name
    req => req.RequestId,  // Request ID Extractor
    ack => ack.RequestId   // Ack ID Extractor
);
```

### 3. Send a Command

```csharp
var uniqueId = Guid.NewGuid();
var request = new CreateEntityRequest { RequestId = uniqueId, EntityId = 123 };

try
{
    var ack = await client.SendAsync(request, timeoutMs: 5000);
    Console.WriteLine($"Command Success: {ack.ResultCode}");
}
catch (TimeoutException)
{
    Console.WriteLine("Command timed out!");
}
```

## Architecture

The `DdsCommandClient` maintains a `ConcurrentDictionary` of pending `TaskCompletionSource` objects. A background `AckListenerLoop` reads generic samples from the Ack topic and completes the corresponding task if a match is found based on the extracted ID.
