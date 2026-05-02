# BATCH-16: Complete TASK-P4-003: Decouple IG and CGF from NED + Debt Cleanup

**Batch Number:** BATCH-16  
**Tasks:** TASK-P4-003 (complete), DEBT-010 (CGF NED decoupling), DEBT-011 (IG NED decoupling), DEBT-003, DEBT-004, DEBT-007, DEBT-008  
**Phase:** Phase 4 Completion  
**Estimated Effort:** 12-18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-15 complete

---

## Onboarding & Workflow

### Developer Instructions

This batch fully completes TASK-P4-003 (decouple IG and CGF from NED). After this batch,
`Hrot.CGF.csproj` and `Hrot.IG.csproj` must NOT reference `Hrot.Network.NED`.

Half of this work (CGF decoupling) involves moving/refactoring event types and a pure-ECS
system. The other half (IG decoupling) is more involved: creating a neutral network adapter
interface and refactoring `IgApplication.cs` + related classes.

### Required Reading (IN ORDER)
1. `.github\skills\developer\SKILL.md` - How to work with batches
2. `.dev\modular-2\TASK-DETAIL.md#task-p4-003` - Success conditions  
3. `.dev\modular-2\DEBT-TRACKER.md` - Items DEBT-010, DEBT-011, DEBT-003, DEBT-004, DEBT-007, DEBT-008
4. `.dev\modular-2\reports\BATCH-11-REPORT.md` - Previous partial attempt at P4-003

### Key File Locations
- `Hrot.Network.NED/Systems/MissionControlExecutionSystem.cs` - MOVE to CGF
- `Hrot.Network.NED/Events/MissionControlCqrsEvents.cs` - REFACTOR + MOVE to Core
- `Hrot.Network.NED/Helpers/MissionTriggerHelper.cs` - UPDATE + MOVE to Core
- `Hrot.Network.NED/SimHost/MissionControlIngressTranslator.cs` - UPDATE translation
- `Hrot.CGF/CgfLogicPack.cs` - UPDATE import
- `Hrot.IG/IgApplication.cs` (~2500 lines) - MAJOR REFACTOR
- `Hrot.IG/Systems/MapCommandController.cs` - UPDATE
- `Hrot.IG/Systems/ContextMenuSystem.cs` - UPDATE
- `Hrot.IG/Services/IgCapabilitiesPublisher.cs` - UPDATE
- `Hrot.IG/UI/MiniExConPanelState.cs` - UPDATE
- `Hrot.Core/Network/Commands.cs` - ADD neutral DTOs (already has many)
- `Hrot.Core/Network/INetworkFactory.cs` - ADD factory methods
- `Hrot.Core/Mission/MissionTypes.cs` - ADD neutral command payload type

### Report Submission
**When done, submit your report to:**  
`.dev/modular-2/reports/BATCH-16-REPORT.md`

---

## Context

This batch is the final push to remove `Hrot.Network.NED` references from `Hrot.CGF`
and `Hrot.IG`. Previous attempts (BATCH-10, BATCH-11) made partial progress:
- `Hrot.Orchestrator` is already decoupled (BATCH-11)
- 3 of 5 IG translators are already in `Hrot.Network.NED/IG/`
- The two remaining translators are blocked by circular dep (types live in Hrot.IG)
- `IIgTranslators` interface and `NedIgTranslators` exist in Core/NED respectively
- `MissionControlExecutionSystem` uses DDS mission trigger type from NED

---

## Objectives

1. **CGF decoupling**: Move `MissionControlExecutionSystem` to `Hrot.CGF/Systems/` without any NED dependency
2. **IG decoupling**: Create `IIgNetworkAdapter` abstraction; refactor all 5 NED-using IG classes
3. **P3 debt cleanup**: resolve DEBT-003, DEBT-004, DEBT-007, DEBT-008

---

## Tasks

---

### Task 1: Neutral Mission Command Types in Hrot.Core (CGF Precursor)

**File:** `Hrot.Core/Mission/MissionTypes.cs` (UPDATE — add new type)

`MissionTask` in this file already has `List<MissionTrigger> Triggers` using the neutral
`MissionTrigger` type. We need a neutral replacement for the DDS `MissionCommandUnion`.

**Add at the bottom of `Hrot.Core/Mission/MissionTypes.cs`:**

```csharp
/// <summary>
/// Protocol-neutral carrier for a mission command payload.
/// Replaces the DDS-generated <c>MissionCommandUnion</c> discriminated-union type
/// in event routing between the translator layer and the execution system.
/// </summary>
public sealed class MissionCommandPayload
{
    /// <summary>Discriminator — identifies which command this payload represents.</summary>
    public eMissionCommandType CommandType { get; set; }

    /// <summary>Full mission plan; populated for <see cref="eMissionCommandType.CMD_REPLACE_MISSION"/>.</summary>
    public MissionPlan? FullMissionData { get; set; }

    /// <summary>Target task ID; populated for <see cref="eMissionCommandType.CMD_JUMP_TO_TASK"/>.</summary>
    public Guid TargetTaskId { get; set; }
}
```

---

### Task 2: Move MissionControlCqrsEvents to Hrot.Core (CGF Precursor)

**File:** `Hrot.Core/Events/MissionControlCqrsEvents.cs` (NEW FILE)

Create a new file in `Hrot.Core/Events/` (create the `Events/` directory if it doesn't exist).
This replaces the copy in `Hrot.Network.NED/Events/MissionControlCqrsEvents.cs`.

The key change: `MissionControlIntent.Payload` changes from DDS `MissionCommandUnion` to
neutral `MissionCommandPayload`.

```csharp
using System;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Hrot.Core.Mission;

namespace Hrot.Common.Events
{
    /// <summary>
    /// Cross-boundary intent published by <c>MissionControlIngressTranslator</c>
    /// when a mission command must traverse the bus.
    /// This is a managed class (not a value type) because <see cref="MissionCommandPayload"/>
    /// contains managed reference fields (<see cref="MissionPlan"/>, task lists, etc.).
    /// Use <c>FdpEventBus.PublishManaged</c> / <c>ConsumeManaged</c> for routing.
    /// </summary>
    public sealed class MissionControlIntent
    {
        /// <summary>Unique identifier that links this intent to its ACK.</summary>
        public Guid RequestId;

        /// <summary>Network entity ID of the mission target.</summary>
        public long TargetEntityId;

        /// <summary>Client-side version the request is based on (0 = unconditional).</summary>
        public long BaseVersion;

        /// <summary>Strongly-typed neutral mission command payload.</summary>
        public MissionCommandPayload Payload = new();
    }

    /// <summary>
    /// Outcome event published after processing a <see cref="MissionControlIntent"/>.
    /// Unmanaged struct routed via FdpEventBus.Publish / Consume.
    /// </summary>
    [EventId(6002)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MissionControlAckEvent
    {
        /// <summary>Matches the <see cref="MissionControlIntent.RequestId"/> that triggered this ACK.</summary>
        public Guid RequestId;

        /// <summary>Zero on success; non-zero NED status code on failure.</summary>
        public int ErrorCode;

        /// <summary>New version of the mission plan on the entity (0 on failure).</summary>
        public long NewVersion;
    }
}
```

Then **delete** `Hrot.Network.NED/Events/MissionControlCqrsEvents.cs`.

**Important:** All files that currently import `Hrot.Common.Events.MissionControlIntent` or
`Hrot.Common.Events.MissionControlAckEvent` from `Hrot.Network.NED` will now find them in
`Hrot.Core` instead (same namespace, different assembly). Since `Hrot.Network.NED` references
`Hrot.Core`, this should be transparent.

---

### Task 3: Move MissionTriggerHelper to Hrot.Core

**File:** `Hrot.Core/Mission/MissionTriggerHelper.cs` (NEW FILE)

Create a neutral version that accepts `List<Hrot.Core.Mission.MissionTrigger>` (not DDS type).

```csharp
using System.Collections.Generic;
using System.Globalization;
using FDP.Toolkit.Behavior.Components;
using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;

namespace Hrot.Core.Mission
{
    /// <summary>
    /// Shared helper for resolving neutral mission trigger descriptors into
    /// ECS trigger enumerations (<see cref="EcsMissionTrigger"/>).
    /// </summary>
    public static class MissionTriggerHelper
    {
        /// <summary>
        /// Resolves the first trigger in <paramref name="triggers"/> to the corresponding
        /// <see cref="EcsMissionTrigger"/> and numeric parameter.
        /// Returns <c>(TimerElapsed, float.MaxValue)</c> when triggers is null or empty
        /// so that a phase with no trigger holds indefinitely.
        /// </summary>
        public static (EcsMissionTrigger Trigger, float Param) ResolveTrigger(List<MissionTrigger>? triggers)
        {
            if (triggers == null || triggers.Count == 0)
                return (EcsMissionTrigger.TimerElapsed, float.MaxValue);

            var trigger = triggers[0];
            var type = trigger.Type ?? string.Empty;

            return type switch
            {
                "TimerElapsed"       => (EcsMissionTrigger.TimerElapsed,     ParseTriggerParam(trigger.Params)),
                // "ReachedDestination" is the legacy wire string for the navigation-arrival trigger.
                // Per BS1-T022, arrival is now signalled via the BehaviorFinished path.
                // Map to BehaviorFinished at ingress to preserve backward wire compatibility.
                "ReachedDestination" => (EcsMissionTrigger.BehaviorFinished, 0f),
                "HealthCritical"     => (EcsMissionTrigger.HealthCritical,   ParseTriggerParam(trigger.Params)),
                "BehaviorFinished"   => (EcsMissionTrigger.BehaviorFinished, 0f),
                "UnderAttack"        => (EcsMissionTrigger.UnderAttack,      0f),
                _                    => (EcsMissionTrigger.TimerElapsed,     0f)
            };
        }

        /// <summary>
        /// Parses a float parameter string (e.g. "10.5") using invariant culture.
        /// Returns 0f for null, empty, or unparseable input.
        /// </summary>
        public static float ParseTriggerParam(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0f;

            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0f;
        }
    }
}
```

Then **delete** `Hrot.Network.NED/Helpers/MissionTriggerHelper.cs` (or if other NED code still
uses the old `DdsMissionTrigger`-based version, keep the old file and rename it to
`NedMissionTriggerHelper` or update internal callers).

Check if anything in `Hrot.Network.NED` still calls `MissionTriggerHelper.ResolveTrigger` with
the DDS type — primarily `EntityMissionIngressTranslator.cs`. If so, update that call site to
map DDS triggers to neutral triggers first, then call the new helper.

---

### Task 4: Update MissionControlIngressTranslator (NED)

**File:** `Hrot.Network.NED/SimHost/MissionControlIngressTranslator.cs` (UPDATE)

Translate from DDS `MissionCommandUnion` to neutral `MissionCommandPayload` when publishing
`MissionControlIntent`. Add a `using` for `Hrot.Core.Mission` and `Hrot.Core.Events` (or
use `Hrot.Common.Events` which is the namespace).

The translator currently does:
```csharp
repo.Bus.PublishManaged(new MissionControlIntent
{
    RequestId      = req.RequestId,
    TargetEntityId = req.TargetEntityId,
    BaseVersion    = req.BaseVersion,
    Payload        = req.Payload,   // DDS MissionCommandUnion
});
```

Change to:
```csharp
repo.Bus.PublishManaged(new MissionControlIntent
{
    RequestId      = req.RequestId,
    TargetEntityId = req.TargetEntityId,
    BaseVersion    = req.BaseVersion,
    Payload        = MapToNeutralPayload(req.Payload),
});
```

Add private static helper `MapToNeutralPayload` in the same file:
```csharp
private static MissionCommandPayload MapToNeutralPayload(MissionCommandUnion dds)
{
    var payload = new MissionCommandPayload
    {
        CommandType  = (eMissionCommandType)(int)dds._d,
        TargetTaskId = dds._d == Hrot.NED.Messages.eMissionCommandType.CMD_JUMP_TO_TASK
                       ? dds.TargetTaskId
                       : Guid.Empty,
    };

    if (dds._d == Hrot.NED.Messages.eMissionCommandType.CMD_REPLACE_MISSION
        && dds.FullMissionData != null)
    {
        payload.FullMissionData = new MissionPlan
        {
            ActiveTaskId = dds.FullMissionData.ActiveTaskId,
            Tasks = dds.FullMissionData.Tasks?.ConvertAll(MapToNeutralTask) ?? new(),
        };
    }

    return payload;
}

private static MissionTask MapToNeutralTask(Hrot.NED.Descriptors.MissionTask dds)
    => new MissionTask
    {
        TaskId          = dds.TaskId,
        ExecutingEngine = dds.ExecutingEngine ?? string.Empty,
        BehaviorId      = dds.BehaviorId      ?? string.Empty,
        BehaviorParams  = dds.BehaviorParams  ?? string.Empty,
        State           = (eTaskState)(int)dds.State,
        Triggers        = dds.Triggers?.ConvertAll(t => new Hrot.Core.Mission.MissionTrigger
                          { Type = t.Type ?? string.Empty, Params = t.Params ?? string.Empty })
                          ?? new(),
    };
```

You may need to add `using Hrot.Core.Mission;` and `using Hrot.Core.Events` (or
`using Hrot.Common.Events`) to the file. Keep existing `using Hrot.NED.*` for the DDS
source types.

**Note:** Check what namespace `eMissionCommandType` is in both neutral (Hrot.Core.Mission)
and DDS (Hrot.NED.Messages). The cast `(eMissionCommandType)(int)dds._d` assumes matching
int values — verify this is correct (both are defined with the same 0-4 ordering).

---

### Task 5: Move MissionControlExecutionSystem to Hrot.CGF

**Source:** `Hrot.Network.NED/Systems/MissionControlExecutionSystem.cs`  
**Destination:** `Hrot.CGF/Systems/MissionControlExecutionSystem.cs`

Update the file:
1. Remove `using Hrot.NED.Descriptors;` and `using Hrot.NED.Messages;`
2. Remove `using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;`
3. Remove `using NedStatusCode = Hrot.NED.Messages.NedStatusCode;` — replace all `NedStatusCode.X` usages with int literals matching the enum values:
   - `NedStatusCode.Success` → `0`
   - `NedStatusCode.EntityNotFound` → use the int constant (check the enum value from `Hrot.NED.Messages.NedStatusCode` or `Hrot.Network.Orchestration.OrchestrationMessages`)
   - Actually: `NedStatusCode` was moved to `Hrot.Network.Orchestration`. Add `using Hrot.NED.Messages;` pointing to Orchestration, OR use the int values directly. Since `Hrot.CGF` references `Hrot.Network.Orchestration`, you can `using Hrot.NED.Messages;` to get `NedStatusCode` from there.
4. Update `using EcsMissionTrigger = FDP.Toolkit.Behavior.Components.MissionTrigger;` — keep this
5. Instead of `using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;` use `using MissionTrigger = Hrot.Core.Mission.MissionTrigger;` (or just `using Hrot.Core.Mission;` and use `MissionTrigger` directly)
6. Change `MissionControlIntent.Payload._d` → `MissionControlIntent.Payload.CommandType`
7. Change `intent.Payload.FullMissionData` stays the same (same property name in neutral type)
8. Change `intent.Payload.TargetTaskId` stays the same
9. In `TryBuildQueue`, update `ResolveTrigger(task.Triggers)` — `task.Triggers` is now `List<Hrot.Core.Mission.MissionTrigger>` (neutral)
10. Update `ResolveTrigger` to call `Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger(triggers)`
11. Add `using Hrot.Core.Mission;` and `using Hrot.Common.Events;` (which now resolve from Hrot.Core)

**Delete** `Hrot.Network.NED/Systems/MissionControlExecutionSystem.cs` after copying.

`Hrot.CGF.csproj` already references:
- `Hrot.Network.Orchestration` (for NedStatusCode)
- `Hrot.Common` (which references Hrot.Core, giving access to Hrot.Core.Mission and Hrot.Common.Events)
- `Fdp.Engine` (for ECS types, `EcsMissionTrigger`)
- `Fdp.Core` (ComponentSystem, EntityRepository, etc.)

**No** new project references need to be added to `Hrot.CGF.csproj`.

---

### Task 6: Remove NED Reference from Hrot.CGF.csproj

**File:** `Hrot.CGF/Hrot.CGF.csproj` (UPDATE)

Remove the line:
```xml
<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
```

**Verify:** `dotnet build Hrot.CGF\Hrot.CGF.csproj` must pass with 0 errors.

---

### Task 7: Update Test Files Using MissionCommandUnion

The following test files create `MissionControlIntent` with `MissionCommandUnion`.
They must be updated to use `MissionCommandPayload`.

**Files to update:**

1. `Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs`
2. `Hrot.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs`
3. `Hrot.ClusterRunner.Integration.Tests/CgfSubsystemHeadlessTests.cs`
4. `Hrot.ClusterRunner.Integration.Tests/MissionControlIntegrationTests.cs`

**Pattern to change:**
```csharp
// OLD:
new MissionControlIntent
{
    Payload = new MissionCommandUnion
    {
        _d = eMissionCommandType.CMD_REPLACE_MISSION,
        FullMissionData = new MissionPlan { ... }
    }
}

// NEW:
new MissionControlIntent
{
    Payload = new MissionCommandPayload
    {
        CommandType  = eMissionCommandType.CMD_REPLACE_MISSION,
        FullMissionData = new MissionPlan { ... }
    }
}
```

For `CMD_JUMP_TO_TASK` intents:
```csharp
// OLD: Payload = new MissionCommandUnion { _d = eMissionCommandType.CMD_JUMP_TO_TASK, TargetTaskId = taskId }
// NEW: Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_JUMP_TO_TASK, TargetTaskId = taskId }
```

For `CMD_ABORT_ALL`:
```csharp
// NEW: Payload = new MissionCommandPayload { CommandType = eMissionCommandType.CMD_ABORT_ALL }
```

Also update `using` directives: remove `using Hrot.NED.Messages;` where it was only needed
for `MissionCommandUnion`. Keep it where `MissionControlRequest`, `NedStatusCode`, etc. are
still used.

`MissionTask.Triggers` in test fixture code: if tests currently set `Triggers` to a list of
`Hrot.NED.Descriptors.MissionTrigger`, update to use `Hrot.Core.Mission.MissionTrigger` instead:
```csharp
// OLD: Triggers = new List<Hrot.NED.Descriptors.MissionTrigger> { new() { Type = "TimerElapsed", Params = "5" } }
// NEW: Triggers = new List<Hrot.Core.Mission.MissionTrigger> { new() { Type = "TimerElapsed", Params = "5" } }
```

---

### Task 8: Create IIgNetworkAdapter Interface in Hrot.Core

**File:** `Hrot.Core/Network/IIgNetworkAdapter.cs` (NEW FILE)

```csharp
using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;

namespace Hrot.Core.Network
{
    /// <summary>
    /// Protocol-neutral abstraction over all DDS write/read operations performed by the
    /// Image Generator application layer.
    ///
    /// <para><b>Design note:</b> The IG application creates a <see cref="DdsParticipant"/>
    /// and passes it to the factory. The factory (e.g. NedNetworkFactory) constructs the
    /// concrete adapter that owns all DDS writer and reader instances.  The IG itself only
    /// calls this interface — it never references any Hrot.NED type directly.</para>
    /// </summary>
    public interface IIgNetworkAdapter : IDisposable
    {
        // ── Egress (IG → network) ─────────────────────────────────────────────

        /// <summary>Publishes a map-click event.</summary>
        void WriteMapClick(MapClickEventDto dto);

        /// <summary>Publishes a selection-changed event.</summary>
        void WriteSelectionChanged(SelectionChangedEventDto dto);

        /// <summary>Publishes a map-command ACK (response to ExCon tool-activation commands).</summary>
        void WriteMapCommandAck(MapCommandAckDto dto);

        /// <summary>
        /// Publishes a ContextMenuRequest so ExCon can push back an action list when
        /// the IG has no cached actions for an entity.
        /// </summary>
        void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection);

        /// <summary>
        /// Publishes the IG capabilities announcement (layer tree, supported tools).
        /// Called once on startup.
        /// </summary>
        void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson);

        // ── Ingress (network → IG) — polled once per frame ────────────────────

        /// <summary>
        /// Polls the MapInteractionConfig topic.
        /// Returns null when there is no new sample.
        /// </summary>
        MapConfigDto? PollMapConfig();

        /// <summary>
        /// Polls the MapCommandRequest topic.
        /// Returns null when there is no new command.
        /// </summary>
        MapCommandDto? PollMapCommand();

        /// <summary>
        /// Polls the CreateUpdateDeleteEntityAck topic.
        /// Returns null when there is no new ACK.
        /// </summary>
        EntityLifecycleAckDto? PollEntityLifecycleAck();

        // ── Command gateway ───────────────────────────────────────────────────

        /// <summary>
        /// Neutral command gateway for create-entity / update-descriptor / mission-control
        /// requests initiated by the IG operator (e.g. MiniExConPanel, drag-drop).
        /// </summary>
        ICommandGateway CommandGateway { get; }
    }

    /// <summary>No-op implementation used in offline / headless / editor mode.</summary>
    public sealed class NullIgNetworkAdapter : IIgNetworkAdapter
    {
        /// <summary>Shared singleton instance.</summary>
        public static readonly NullIgNetworkAdapter Instance = new();

        public void WriteMapClick(MapClickEventDto dto) { }
        public void WriteSelectionChanged(SelectionChangedEventDto dto) { }
        public void WriteMapCommandAck(MapCommandAckDto dto) { }
        public void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection) { }
        public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson) { }
        public MapConfigDto? PollMapConfig() => null;
        public MapCommandDto? PollMapCommand() => null;
        public EntityLifecycleAckDto? PollEntityLifecycleAck() => null;
        public ICommandGateway CommandGateway => NullCommandGateway.Instance;
        public void Dispose() { }
    }
}
```

**Note:** `NullCommandGateway` — check if one already exists in `Hrot.Core`. If not, create
a simple stub:
```csharp
internal sealed class NullCommandGateway : ICommandGateway
{
    public static readonly NullCommandGateway Instance = new();
    public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default) => Task.FromResult(0);
    public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default) => Task.CompletedTask;
    public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
        => Task.FromResult(new MissionCommitResult { Success = false });
    public void Dispose() { }
}
```

---

### Task 9: Create NedIgNetworkAdapter in Hrot.Network.NED

**File:** `Hrot.Network.NED/IG/NedIgNetworkAdapter.cs` (NEW FILE)

This is the NED (DDS) implementation of `IIgNetworkAdapter`. It owns all IG DDS writers
and readers.

```csharp
using System;
using System.Collections.Generic;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// NED/DDS implementation of <see cref="IIgNetworkAdapter"/>.
    /// Owns all DDS writers and readers used by the IG application.
    /// Created by <see cref="Hrot.Network.NED.NedNetworkFactory.CreateIgNetworkAdapter"/>.
    /// </summary>
    public sealed class NedIgNetworkAdapter : IIgNetworkAdapter
    {
        private readonly DdsWriter<MapClickEvent>              _clickWriter;
        private readonly DdsWriter<SelectionChangedEvent>      _selectionWriter;
        private readonly DdsWriter<MapCommandAck>              _ackWriter;
        private readonly DdsWriter<ContextMenuRequest>         _contextMenuWriter;
        private readonly DdsReader<MapInteractionConfig>       _configReader;
        private readonly DdsReader<MapCommandRequest>          _commandReader;
        private readonly DdsReader<CreateUpdateDeleteEntityAck> _ackReader;
        private readonly ICommandGateway                       _commandGateway;
        private bool _disposed;

        /// <inheritdoc/>
        public ICommandGateway CommandGateway => _commandGateway;

        public NedIgNetworkAdapter(DdsParticipant participant, long nodeId = 0)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            _clickWriter       = new DdsWriter<MapClickEvent>(participant, "MapClickEvent");
            _selectionWriter   = new DdsWriter<SelectionChangedEvent>(participant, "SelectionChangedEvent");
            _ackWriter         = new DdsWriter<MapCommandAck>(participant, "MapCommandAck");
            _contextMenuWriter = new DdsWriter<ContextMenuRequest>(participant, "ContextMenuRequest");
            _configReader      = new DdsReader<MapInteractionConfig>(participant);
            _commandReader     = new DdsReader<MapCommandRequest>(participant, "MapCommandRequest");
            _ackReader         = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");
            _commandGateway    = new Hrot.Map.Common.Commands.NedCommandGateway(participant, nodeId);
        }

        public void WriteMapClick(MapClickEventDto dto)
        {
            _clickWriter.Write(new MapClickEvent
            {
                InteractionContextId = dto.InteractionContextId,
                Latitude             = dto.Latitude,
                Longitude            = dto.Longitude,
                Altitude             = dto.Altitude,
            });
        }

        public void WriteSelectionChanged(SelectionChangedEventDto dto)
        {
            _selectionWriter.Write(new SelectionChangedEvent
            {
                MapId             = dto.MapId,
                SelectedEntityIds = new System.Collections.Generic.List<int>(dto.SelectedEntityIds),
            });
        }

        public void WriteMapCommandAck(MapCommandAckDto dto)
        {
            _ackWriter.Write(new MapCommandAck
            {
                RequestId  = dto.RequestId,
                StatusCode = dto.StatusCode,
                DataJson   = dto.DataJson ?? string.Empty,
            });
        }

        public void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection)
        {
            _contextMenuWriter.Write(new ContextMenuRequest
            {
                RequestId    = requestId,
                MapId        = mapId,
                ForSelection = new System.Collections.Generic.List<int>(forSelection),
            });
        }

        public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson)
        {
            try
            {
                using var writer = new DdsWriter<IGCapabilitiesAnnounce>(
                    ((Hrot.Map.Common.Commands.NedCommandGateway)_commandGateway)
                        .GetParticipant(),
                    "IGCapabilitiesAnnounce");

                writer.Write(new IGCapabilitiesAnnounce
                {
                    MapId                    = mapId,
                    LayerTreeJson            = layerTreeJson,
                    ConfigurationSchemasJson = configSchemasJson,
                    OverlayStyleSchemaJson   = string.Empty,
                    TkbManifestJson          = string.Empty,
                });
            }
            catch (Exception ex)
            {
                FdpLog<NedIgNetworkAdapter>.Warn("[Node-{0}] Failed to publish IGCapabilitiesAnnounce: {1}", mapId, ex.Message);
            }
        }

        public MapConfigDto? PollMapConfig()
        {
            using var loan = _configReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var d = sample.Data;
                return new MapConfigDto
                {
                    ActiveContextId = d.ActiveContextId,
                    ConfigJson      = d.ConfigJson ?? string.Empty,
                };
            }
            return null;
        }

        public MapCommandDto? PollMapCommand()
        {
            using var loan = _commandReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var d = sample.Data;
                return new MapCommandDto
                {
                    RequestId       = d.RequestId,
                    TargetMapId     = d.TargetMapId,
                    CommandType     = d.CommandType ?? string.Empty,
                    CommandArgsJson = d.CommandArgsJson ?? string.Empty,
                };
            }
            return null;
        }

        public EntityLifecycleAckDto? PollEntityLifecycleAck()
        {
            using var loan = _ackReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var d = sample.Data;
                return new EntityLifecycleAckDto
                {
                    RequestId  = d.RequestId,
                    EntityId   = d.EntityId,
                    StatusCode = d.StatusCode,
                };
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clickWriter.Dispose();
            _selectionWriter.Dispose();
            _ackWriter.Dispose();
            _contextMenuWriter.Dispose();
            _configReader.Dispose();
            _commandReader.Dispose();
            _ackReader.Dispose();
            _commandGateway.Dispose();
        }
    }
}
```

**IMPORTANT notes on NedIgNetworkAdapter:**

1. `MapClickEvent` — check if it has a `HitEntityIds` field; if so, map it from `dto.HitEntityIds`
2. `MapInteractionConfig` — check actual field names; adjust PollMapConfig accordingly
3. `MapCommandRequest` — check actual fields (TargetMapId, CommandType, CommandArgsJson)
   Match the field names exactly to what's in the DDS-generated type.
4. `PublishCapabilities` uses a new DdsWriter created per-call (like the original) but needs
   the participant. If `NedCommandGateway.GetParticipant()` doesn't exist, add it or store a
   participant reference directly in `NedIgNetworkAdapter`.

**Alternative for PublishCapabilities participant access:** Just keep a `_participant` field:
```csharp
private readonly DdsParticipant _participant;
// in constructor: _participant = participant;
// in PublishCapabilities: using var writer = new DdsWriter<IGCapabilitiesAnnounce>(_participant, "IGCapabilitiesAnnounce");
```

---

### Task 10: Update INetworkFactory to Add IIgNetworkAdapter

**File:** `Hrot.Core/Network/INetworkFactory.cs` (UPDATE)

Add to the interface:
```csharp
/// <summary>
/// Creates the IG network adapter wrapping all DDS writers and readers for the IG.
/// Pass <c>null</c> for <paramref name="participant"/> in headless/offline mode.
/// </summary>
IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? participant, long nodeId = 0);
```

---

### Task 11: Implement CreateIgNetworkAdapter in All Factories

**Files to update:**

1. `Hrot.Network.NED/NedNetworkFactory.cs` — return `new NedIgNetworkAdapter(participant, nodeId)`  
   (if participant is null, return `NullIgNetworkAdapter.Instance`)

2. `Hrot.Network.BDC/BdcNetworkFactory.cs` — return `NullIgNetworkAdapter.Instance`

3. `Hrot.Editor/OfflineNetworkFactory.cs` — return `NullIgNetworkAdapter.Instance`

---

### Task 12: Move IgWeaponFireEvent, ContextActionsUpdate, ContextAction to Hrot.Common

**Context:** `WeaponFireIngressTranslator` in Hrot.IG publishes `IgWeaponFireEvent` but
cannot be moved to `Hrot.Network.NED/IG/` because `IgWeaponFireEvent` is defined in Hrot.IG,
creating a circular dependency. Same for `ContextActionsUpdateTranslator` and
`ContextActionsUpdate`/`ContextAction`.

**Step A:** Create `Hrot.Common/Events/IgCommonEvents.cs`:

```csharp
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Hrot.IG
{
    /// <summary>
    /// Published by <c>WeaponFireIngressTranslator</c> when a WeaponFire DDS message is received.
    /// </summary>
    [EventId(6001)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct IgWeaponFireEvent
    {
        public long ShooterEntityId;
        public long TargetEntityId;
        public int WeaponIndex;
    }

    /// <summary>
    /// Sent from ExCon to IG to update the list of context-menu actions for an entity.
    /// </summary>
    public sealed class ContextActionsUpdate
    {
        public int EntityNetworkId { get; init; }
        public System.Collections.Generic.List<Hrot.IG.Components.ContextAction> Actions { get; init; } = new();
    }
}
```

**Step B:** Create `Hrot.Common/Components/ContextAction.cs`:

```csharp
namespace Hrot.IG.Components
{
    /// <summary>A single action entry in a context menu.</summary>
    public sealed class ContextAction
    {
        public string Label { get; init; } = string.Empty;
        public string ActionName { get; init; } = string.Empty;
    }
}
```

Note: Keep these types in the `Hrot.IG` and `Hrot.IG.Components` namespaces even though
they live in `Hrot.Common` assembly. This preserves all using directives throughout the codebase.

**Step C:** Delete the originals from `Hrot.IG`:
- Remove `IgWeaponFireEvent`, `ContextActionsUpdate` from `Hrot.IG/IgEvents.cs`
  (keep `ContextActionTriggered` in Hrot.IG — it doesn't need to move)
- Remove `ContextAction` from `Hrot.IG/Components/ContextMenuState.cs`

**Add `Hrot.Common` project reference to `Hrot.Network.NED.csproj`** so the translator can
find these types. (Hrot.Network.NED already references Hrot.Common.)

---

### Task 13: Move Remaining IG Translators to Hrot.Network.NED/IG/

**Files to move:**
- `Hrot.IG/Translators/WeaponFireIngressTranslator.cs` → `Hrot.Network.NED/IG/WeaponFireIngressTranslator.cs`
- `Hrot.IG/Translators/ContextActionsUpdateTranslator.cs` → `Hrot.Network.NED/IG/ContextActionsUpdateTranslator.cs`

**Update namespaces** from `Hrot.IG.Translators` to `Hrot.Network.NED.IG`.

These translators use `IgWeaponFireEvent` and `ContextActionsUpdate` — after Task 12,
these types are now in `Hrot.Common` (but same namespace `Hrot.IG` / `Hrot.IG.Components`),
so no `using` changes are needed.

**Update `NedIgTranslators.cs`** to include the two new translators:

```csharp
// Add to GetTranslators() in NedIgTranslators.cs:
translators.Add(new WeaponFireIngressTranslator(participant, entityMap));
// ContextActionsUpdateTranslator needs ghostCreationSystem and eventBus:
if (ghostCreationSystem != null)
    translators.Add(new ContextActionsUpdateTranslator(
        participant, entityMap, bus, ghostCreationSystem, localNodeId));
```

---

### Task 14: Refactor MapCommandController (Remove DDS Types)

**File:** `Hrot.IG/Systems/MapCommandController.cs` (UPDATE)

Replace `IDdsWriter<MapCommandAck>` with neutral callback/adapter:

1. Change constructor parameter:
   ```csharp
   // OLD:
   public MapCommandController(MapCanvas canvas, FdpEventBus eventBus, IDdsWriter<MapCommandAck> ackWriter, long localNodeId = 0)
   // NEW:
   public MapCommandController(MapCanvas canvas, FdpEventBus eventBus, Action<MapCommandAckDto> ackCallback, long localNodeId = 0)
   ```

2. Change `_ackWriter` field to `_ackCallback: Action<MapCommandAckDto>`.

3. Update `PublishAck(long statusCode, string dataJson)`:
   ```csharp
   private void PublishAck(long statusCode, string dataJson)
   {
       _ackCallback(new MapCommandAckDto
       {
           RequestId  = _sessionRequestId,
           StatusCode = (int)statusCode,
           DataJson   = dataJson,
       });
       // ... logging unchanged
   }
   ```

4. Update `OnCreateEntityAck(CreateUpdateDeleteEntityAck ack)` → `OnCreateEntityAck(EntityLifecycleAckDto ack)`:
   - `ack.StatusCode` stays the same
   - `ack.RequestId` stays the same
   - `ack.EntityId` stays the same
   - Remove `using Hrot.NED.Messages;` (if only used for NedStatusCode)
   - Replace `(int)NedStatusCode.InProgress` with `EntityLifecycleAckDto.StatusInProgress` (= 1)

5. Remove `using Hrot.NED.Messages;` and `using Hrot.NED.Common;` from the file.

**Note:** `IgApplication` calls `_mapCommandController.OnCreateEntityAck(ack)` where `ack`
comes from polling the DDS reader. After Task 16, this polling returns `EntityLifecycleAckDto`.

---

### Task 15: Refactor ContextMenuSystem (Remove DDS Types)

**File:** `Hrot.IG/Systems/ContextMenuSystem.cs` (UPDATE)

Replace `IDdsWriter<ContextMenuRequest>?` with `Action<Guid, int, IReadOnlyList<int>>?`:

1. Change `_contextMenuRequestWriter` field type from `IDdsWriter<ContextMenuRequest>?` to
   `Action<Guid, int, IReadOnlyList<int>>?`

2. Update `SetCacheMissWriter(IDdsWriter<ContextMenuRequest>? writer, int mapId)`:
   ```csharp
   internal void SetContextMenuCallback(Action<Guid, int, IReadOnlyList<int>>? callback, int mapId)
   {
       _contextMenuRequestWriter = callback;
       _mapId = mapId;
   }
   ```
   (Or keep the method name `SetCacheMissWriter` but change the parameter type — both work)

3. Update the write call:
   ```csharp
   // OLD:
   _contextMenuRequestWriter.Write(new ContextMenuRequest { RequestId = ..., MapId = ..., ForSelection = ... });
   // NEW:
   _contextMenuRequestWriter?.Invoke(requestId, _mapId, new List<int> { (int)netId.Value });
   ```

4. Remove `using Hrot.NED.Messages;` from the file.

---

### Task 16: Refactor IgCapabilitiesPublisher (Remove DDS Types)

**File:** `Hrot.IG/Services/IgCapabilitiesPublisher.cs` (UPDATE)

Replace DDS participant + DDS type with neutral parameters:

```csharp
/// <summary>
/// Invokes the network adapter to publish IG capabilities.
/// </summary>
/// <param name="adapter">Network adapter (no-op if null).</param>
/// <param name="mapId">IG instance ID.</param>
public static void Publish(IIgNetworkAdapter? adapter, int mapId)
{
    if (adapter == null) return;
    adapter.PublishCapabilities(mapId, BuildLayerTreeJson(), BuildConfigSchemasJson());
}
```

Remove the `DdsParticipant` parameter, `DdsWriter<IGCapabilitiesAnnounce>` usage, and the
`using Hrot.NED.Descriptors;` / `using CycloneDDS.Runtime;` imports.

Add `using Hrot.Core.Network;` for `IIgNetworkAdapter`.

---

### Task 17: Refactor MiniExConPanelState (Remove NedCommandGateway)

**File:** `Hrot.IG/UI/MiniExConPanelState.cs` (UPDATE)

Replace `NedCommandGateway?` with `ICommandGateway?` (from `Hrot.Core.Network`).

1. Change `SubmitViaGateway(NedCommandGateway? gateway)`:
   - Accepts `ICommandGateway? gateway`
   - Replace construction of `CreateEntityRequest` (DDS type) with `CreateEntityCommand` (neutral):
   ```csharp
   var cmd = new CreateEntityCommand
   {
       TkbType        = TkbType,
       Latitude       = latitude,    // from position
       Longitude      = longitude,
       Altitude       = 0,
       ForceId        = (int)Affiliation,
       PropertiesJson = null,
   };
   var entityId = await gateway.CreateEntityAsync(cmd);
   ```

2. Change `SubmitWithWanderMissionViaGateway(NedCommandGateway? gateway)`:
   - Accepts `ICommandGateway? gateway`
   - Build neutral create + mission commands

3. Remove `using Hrot.NED.Descriptors;`, `using Hrot.NED.Messages;`, `using Hrot.NED.Common;`
4. Add `using Hrot.Core.Network;`

**Note:** The original `SubmitViaGateway` may use geographic coordinate conversion
(`IGeographicTransform`) and build entity descriptors. Preserve this logic, just replace the
DDS request types. The `ICommandGateway.CreateEntityAsync(CreateEntityCommand cmd, ...)` takes
the position via `Latitude`, `Longitude`, `Altitude` fields of `CreateEntityCommand`.

---

### Task 18: Major Refactor of IgApplication (Remove All NED References)

**File:** `Hrot.IG/IgApplication.cs` (UPDATE — ~2500 lines)

This is the most complex change. The goal: remove all `Hrot.NED.*` usages from `IgApplication`.

**Step A: Add `_networkAdapter` field and update `InitializeEmbedded` signature:**

```csharp
// New field:
private IIgNetworkAdapter? _networkAdapter;

// Update signature (add networkFactory param):
public void InitializeEmbedded(
    bool headless = false,
    int? domainIdOverride = null,
    int nodeIdOverride = 0,
    INetworkFactory? networkFactory = null,
    IIgTranslators? igTranslatorsProvider = null)
```

**Step B: In `InitializeNetwork` method, replace DDS creation with adapter:**

```csharp
// REMOVE all of these:
_clickWriter = new DdsWriter<MapClickEvent>(participant, "MapClickEvent");
_selectionWriter = new DdsWriter<SelectionChangedEvent>(participant, "SelectionChangedEvent");
_configReader = new DdsReader<MapInteractionConfig>(participant);
_commandReader = new DdsReader<MapCommandRequest>(participant, "MapCommandRequest");
_mapCommandAckWriter = new DdsWriter<MapCommandAck>(participant, "MapCommandAck");
_createEntityAckReader = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");
_contextMenuRequestWriter = new DdsWriter<ContextMenuRequest>(participant, "ContextMenuRequest");
// Also: NedCommandGateway creation

// REPLACE WITH:
_networkAdapter = networkFactory?.CreateIgNetworkAdapter(participant, _nodeId)
                  ?? NullIgNetworkAdapter.Instance;
_commandGateway = _networkAdapter.CommandGateway;
```

**Step C: Update MapCommandController construction:**

```csharp
// OLD:
_mapCommandController = new MapCommandController(_canvas, _eventBus, _mapCommandAckWriter, _nodeId);

// NEW:
_mapCommandController = new MapCommandController(
    _canvas,
    _eventBus,
    dto => _networkAdapter.WriteMapCommandAck(dto),
    _nodeId);
```

**Step D: Update ContextMenuSystem wiring:**

```csharp
// OLD:
_contextMenuSystem.SetCacheMissWriter(_contextMenuRequestWriter, _mapId);

// NEW:
_contextMenuSystem.SetContextMenuCallback(
    (reqId, mapId, sel) => _networkAdapter.WriteContextMenuRequest(reqId, mapId, sel),
    _mapId);
```

**Step E: Update IgCapabilitiesPublisher call:**

```csharp
// OLD:
IgCapabilitiesPublisher.Publish(participant, _mapId);

// NEW:
IgCapabilitiesPublisher.Publish(_networkAdapter, _mapId);
```

**Step F: Update polling loop (Update method):**

Find the frame-update loop where the app polls DDS:
```csharp
// OLD: polling MapInteractionConfig
using var cfgLoan = _configReader?.Take();
...

// NEW:
var cfgDto = _networkAdapter?.PollMapConfig();
if (cfgDto != null)
    HandleMapInteractionConfig(cfgDto);

// OLD polling MapCommandRequest:
using var cmdLoan = _commandReader?.Take();
...

// NEW:
var cmdDto = _networkAdapter?.PollMapCommand();
if (cmdDto != null)
    HandleMapCommand(cmdDto);

// OLD polling CreateUpdateDeleteEntityAck:
using var ackLoan = _createEntityAckReader?.Take();
...

// NEW:
var ackDto = _networkAdapter?.PollEntityLifecycleAck();
if (ackDto != null)
    _mapCommandController?.OnCreateEntityAck(ackDto);
```

**Step G: Update MapClickEvent + SelectionChanged publishing:**

Find places where `_clickWriter.Write(...)` and `_selectionWriter.Write(...)` are called.
Replace with `_networkAdapter?.WriteMapClick(new MapClickEventDto {...})` etc.

**Step H: Update MiniExConPanelState gateway references:**

Find where `IgApplication` passes `_commandGateway` (as `NedCommandGateway`) to
`MiniExConPanelState`. After Task 11, `_commandGateway` is `ICommandGateway`, so just pass it.

**Step I: Remove NED type fields and fix HandleMapInteractionConfig/HandleMapCommand:**

Rename internal handlers to accept neutral DTOs instead of DDS types. For example:
```csharp
// OLD:
private void HandleMapCommand(MapCommandRequest req) { ... }

// NEW:
private void HandleMapCommand(MapCommandDto cmd)
{
    // Use cmd.CommandType, cmd.RequestId, cmd.CommandArgsJson
    // Replace switch on DDS-specific string constants if needed
}
```

**Step J: Remove DDS-specific using directives from IgApplication.cs:**

Remove:
```csharp
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
```

And remove DDS field declarations:
```csharp
private DdsWriter<MapClickEvent>? _clickWriter;
private DdsWriter<SelectionChangedEvent>? _selectionWriter;
...
```

**Step K: Fix IgSubsystem to pass INetworkFactory:**

**File:** `Hrot.IG/IgSubsystem.cs` (UPDATE)

```csharp
public void Initialize(SubsystemConfig config)
{
    _headless = config.Headless;
    _app = new IgApplication();
    int? domainOverride = config.DomainId;
    _app.InitializeEmbedded(
        headless: config.Headless,
        domainIdOverride: domainOverride,
        nodeIdOverride: config.NodeId,
        networkFactory: config.Headless ? null : new Hrot.Network.NED.NedNetworkFactory(),
        igTranslatorsProvider: new Hrot.Network.NED.IG.NedIgTranslators());
}
```

Or if `NedNetworkFactory` requires domain ID: `new Hrot.Network.NED.NedNetworkFactory(config.DomainId ?? 0)`.
Check how `NedNetworkFactory` is currently constructed elsewhere in the codebase.

---

### Task 19: Remove NED Reference from Hrot.IG.csproj

**File:** `Hrot.IG/Hrot.IG.csproj` (UPDATE)

Remove:
```xml
<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
```

**Verify:** `dotnet build Hrot.IG\Hrot.IG.csproj` must pass with 0 errors.

---

### Task 20: P3 Debt Cleanup

#### DEBT-003: Remove developer scripts from .dev/ root

Delete these scripts from the workspace `.dev/` root:
- `fix-component-ids.ps1`
- `fix-empty-refs.ps1`
- `remap-component-ids.ps1`
- `update-refs.ps1`
- `update-solutions.ps1`
- `update-solutions2.ps1`

These were one-off migration helpers from earlier batches and are no longer needed.

#### DEBT-004: Old source directories

Verify that `Fdp.Kernel/`, `FDP.Interfaces/`, and `ModuleHost.Core/` do NOT exist in the
workspace root. If confirmed absent, mark as resolved in DEBT-TRACKER.

#### DEBT-007: DDS crash on exit in Hrot.ExCon.Tests

**File:** `Hrot.ExCon.Tests/DdsWriterAdapterTests.cs`

Add the `[Collection("DDS")]` xunit collection to isolate DDS tests from parallel execution:

```csharp
[Collection("DDS")]
public class DdsWriterAdapterTests { ... }
```

Also check if there are other DDS-using test classes in `Hrot.ExCon.Tests` and add the
same `[Collection("DDS")]` attribute if they also experience crashes.

If a `[CollectionDefinition("DDS")]` marker class doesn't exist yet:

Create `Hrot.ExCon.Tests/DdsTestCollection.cs`:
```csharp
using Xunit;

[CollectionDefinition("DDS", DisableParallelization = true)]
public class DdsTestCollection { }
```

The `DisableParallelization = true` ensures DDS tests run serially even in a parallel test run.

#### DEBT-008: Complete NedTranslationHelper.ToUpdateDescriptorRequest

**File:** `Hrot.Network.NED/ExCon/NedTranslationHelper.cs`

Find `ToUpdateDescriptorRequest` method. Currently it only fills `RequestId`, `EntityId`,
`DescriptorType`, `CurrentVersion`. Add `DescriptorJson` from the command:

```csharp
public static UpdateEntityDescriptorRequest ToUpdateDescriptorRequest(UpdateEntityDescriptorCommand cmd)
{
    return new UpdateEntityDescriptorRequest
    {
        RequestId       = Guid.NewGuid(),
        EntityId        = cmd.EntityId,
        DescriptorType  = dtEntityMaster,
        CurrentVersion  = cmd.BaseVersion,
        DescriptorJson  = cmd.DescriptorJson,   // <-- ADD THIS
    };
}
```

---

## Testing Requirements

After all tasks are complete:

1. `dotnet build IOS-IG-SimHost.sln -v quiet` → 0 errors
2. `dotnet list Hrot.IG\Hrot.IG.csproj reference` → no `Hrot.Network.NED` entry
3. `dotnet list Hrot.CGF\Hrot.CGF.csproj reference` → no `Hrot.Network.NED` entry
4. `dotnet test Hrot.CGF.Tests\Hrot.CGF.Tests.csproj --no-build` → all pass
5. `dotnet test Hrot.IG.Tests\Hrot.IG.Tests.csproj --no-build` → all pass
6. `dotnet test Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build` → all pass (including MissionControlExecutionSystemTests)
7. `dotnet test Hrot.Network.NED.Tests\Hrot.Network.NED.Tests.csproj --no-build` → all pass
8. `dotnet test Hrot.Orchestrator.Tests\Hrot.Orchestrator.Tests.csproj --no-build` → all pass

Pre-existing skipped tests (`SubsystemHeadlessTests`, `CgfSubsystemHeadlessTests`) may remain
skipped — do NOT unskip them (they require a live DDS cluster).

---

## Important Caveats

### Check actual DDS field names

When mapping DDS → neutral DTOs in `NedIgNetworkAdapter`, verify the ACTUAL field names in
the DDS-generated types. The DDS structs are in `Hrot.Network.NED` (look in the IDL `.idl`
files under `Hrot.Network.NED/` or `Hrot.NED/`). Specifically check:
- `MapClickEvent` fields
- `MapInteractionConfig` fields  
- `MapCommandRequest` fields
- `SelectionChangedEvent` fields

If `MapCommandAckDto` or neutral DTOs have different field names than assumed, adjust.

### Check NedCommandGateway.GetParticipant()

If the `DdsParticipant` needs to be accessible from `NedIgNetworkAdapter` for creating the
capabilities writer, the cleanest approach is to store it as a field in `NedIgNetworkAdapter`
directly (not to expose it from NedCommandGateway). Store `_participant` in the adapter.

### eMissionCommandType int mapping

Verify that `(eMissionCommandType)(int)dds._d` is correct. The DDS enum
`Hrot.NED.Messages.eMissionCommandType` and the neutral enum `Hrot.Core.Mission.eMissionCommandType`
must have the same integer values. Check both definitions before doing the cast.

---

## Report Requirements

Submit `.dev/modular-2/reports/BATCH-16-REPORT.md` with:

1. **Phase summary table**: which tasks completed, which had blockers
2. **Reference verification**: output of `dotnet list Hrot.IG reference` and `dotnet list Hrot.CGF reference`
3. **Build result**: full output of `dotnet build IOS-IG-SimHost.sln -v quiet` (warnings count)
4. **Test results**: test pass/fail counts for each affected project
5. **Blockers/decisions**: any design decisions made beyond the spec, or issues encountered
6. **Deferred items**: anything that could not be completed, with root cause

**Insight questions for the report:**
- Were there any unexpected type relationships (types defined in surprising assemblies)?
- Did `MapCommandRequest`/`MapCommandAck`/`MapClickEvent` field names match what was expected?
- Were there any test failures beyond what was attributable to the migration?
- Was the `eMissionCommandType` int-cast valid (same ordering in both enums)?
