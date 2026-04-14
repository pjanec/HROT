using System;
using System.Linq;
using System.Reflection;
using Fdp.Kernel;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests;

/// <summary>
/// CMC-S002 and CMC-S003: Verifies structural and attribute contracts for the
/// CQRS event bus structs and operation intent structs.
/// </summary>
public sealed class FdpOrchestrationCqrsStructTests
{
    // ── CMC-S002: DataPolicy.NoRecord on core event structs ────────────────

    [Fact]
    public void ClusterOpCompletedEvent_HasDataPolicyNoRecord()
    {
        var attr = typeof(ClusterOpCompletedEvent).GetCustomAttribute<DataPolicyAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(DataPolicy.NoRecord, attr.Policy);
    }

    [Fact]
    public void ExecuteNodeOpIntent_HasDataPolicyNoRecord()
    {
        var attr = typeof(ExecuteNodeOpIntent).GetCustomAttribute<DataPolicyAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(DataPolicy.NoRecord, attr.Policy);
    }

    [Fact]
    public void NodeOpCompletedEvent_HasDataPolicyNoRecord()
    {
        var attr = typeof(NodeOpCompletedEvent).GetCustomAttribute<DataPolicyAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(DataPolicy.NoRecord, attr.Policy);
    }

    // ── CMC-S002: Unique EventId attributes on core event structs ──────────

    [Fact]
    public void CoreEventStructs_HaveUniqueEventIds()
    {
        var types = new[] { typeof(ClusterOpCompletedEvent), typeof(ExecuteNodeOpIntent), typeof(NodeOpCompletedEvent) };
        var ids = types
            .Select(t => t.GetCustomAttribute<EventIdAttribute>())
            .Select(a => { Assert.NotNull(a); return a!.Id; })
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains(9011, ids);
        Assert.Contains(9012, ids);
        Assert.Contains(9013, ids);
    }

    // ── CMC-S002: Field structure of ExecuteNodeOpIntent ───────────────────

    [Fact]
    public void ExecuteNodeOpIntent_HasDomainPayloadField_NotPayloadJson()
    {
        var fields = typeof(ExecuteNodeOpIntent).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var fieldNames = fields.Select(f => f.Name).ToArray();

        Assert.Contains("DomainPayload", fieldNames);
        Assert.DoesNotContain("PayloadJson", fieldNames);

        var domainPayloadField = fields.Single(f => f.Name == "DomainPayload");
        Assert.Equal(typeof(object), domainPayloadField.FieldType);
    }

    // ── CMC-S002: Field structure of result event structs ──────────────────

    [Fact]
    public void NodeOpCompletedEvent_HasResultPayloadField_NotResultJson()
    {
        var fields = typeof(NodeOpCompletedEvent).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var fieldNames = fields.Select(f => f.Name).ToArray();

        Assert.Contains("ResultPayload", fieldNames);
        Assert.DoesNotContain("ResultJson", fieldNames);

        var resultPayloadField = fields.Single(f => f.Name == "ResultPayload");
        Assert.Equal(typeof(object), resultPayloadField.FieldType);
    }

    [Fact]
    public void ClusterOpCompletedEvent_HasResultPayloadField_NotResultJson()
    {
        var fields = typeof(ClusterOpCompletedEvent).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var fieldNames = fields.Select(f => f.Name).ToArray();

        Assert.Contains("ResultPayload", fieldNames);
        Assert.DoesNotContain("ResultJson", fieldNames);

        var resultPayloadField = fields.Single(f => f.Name == "ResultPayload");
        Assert.Equal(typeof(object), resultPayloadField.FieldType);
    }

    // ── CMC-S002: ExecuteClusterOpIntent must NOT exist ────────────────────

    [Fact]
    public void ExecuteClusterOpIntent_DoesNotExist()
    {
        var type = typeof(ClusterOpCompletedEvent).Assembly.GetType(
            "FDP.Toolkit.Orchestration.ExecuteClusterOpIntent");
        Assert.Null(type);
    }

    // ── CMC-S002: PublishManaged/ConsumeManaged compile and run ────────────

    [Fact]
    public void FdpEventBus_PublishManagedAndConsumeManaged_ExecuteWithoutException()
    {
        var bus = new FdpEventBus();
        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId = 1,
            Operation = NodeOpType.PrepareLive,
            DomainPayload = null,
        };

        // Publish writes to the current write buffer
        bus.PublishManaged(intent);

        // Swap makes the published events available for consumption
        bus.SwapBuffers();

        // Consume reads from the now-readable buffer
        var consumed = bus.ConsumeManaged<ExecuteNodeOpIntent>();
        Assert.NotEmpty(consumed);
        Assert.Equal(intent.TransactionId, consumed[0].TransactionId);
    }

    // ── CMC-S003: DataPolicy.NoRecord on all intent structs ───────────────

    [Fact]
    public void AllIntentAndEventStructs_HaveDataPolicyNoRecord()
    {
        var cqrsTypes = new Type[]
        {
            typeof(ClusterOpCompletedEvent),
            typeof(ExecuteNodeOpIntent),
            typeof(NodeOpCompletedEvent),
            typeof(TransitionStateIntent),
            typeof(ManageEpisodeIntent),
            typeof(SeekReplayIntent),
            typeof(CancelOperationIntent),
            typeof(ExecuteStorageOpIntent),
            typeof(StorageOpCompletedEvent),
            typeof(TakeCheckpointIntent),
            typeof(LoadZoneIntent),
        };

        foreach (var t in cqrsTypes)
        {
            var attr = t.GetCustomAttribute<DataPolicyAttribute>();
            Assert.True(attr != null, $"{t.Name} is missing [DataPolicy] attribute");
            Assert.Equal(DataPolicy.NoRecord, attr!.Policy);
        }
    }

    // ── CMC-S003: TransitionStateIntent field types ────────────────────────

    [Fact]
    public void TransitionStateIntent_HasClusterStateTargetStateField()
    {
        var field = typeof(TransitionStateIntent)
            .GetField("TargetState", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(field);
        // Must use the FDP domain enum, NOT Hrot.NED
        Assert.Equal(typeof(Fdp.Toolkit.Orchestration.ClusterState), field!.FieldType);
    }

    // ── CMC-S003: ManageEpisodeIntent field types ──────────────────────────

    [Fact]
    public void ManageEpisodeIntent_HasExpectedFields()
    {
        var fields = typeof(ManageEpisodeIntent)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(f => f.Name, f => f.FieldType);

        Assert.True(fields.TryGetValue("IsStart", out var isStartType));
        Assert.Equal(typeof(bool), isStartType);

        Assert.True(fields.TryGetValue("EpisodeId", out var episodeIdType));
        Assert.Equal(typeof(Guid), episodeIdType);

        Assert.True(fields.TryGetValue("ScenarioId", out var scenarioIdType));
        Assert.Equal(typeof(string), scenarioIdType);
    }

    // ── CMC-S003: TakeCheckpointIntent has ONLY RequestId ─────────────────

    [Fact]
    public void TakeCheckpointIntent_HasOnlyRequestIdField()
    {
        var fields = typeof(TakeCheckpointIntent)
            .GetFields(BindingFlags.Public | BindingFlags.Instance);

        Assert.Single(fields);
        Assert.Equal("RequestId", fields[0].Name);
        Assert.Equal(typeof(Guid), fields[0].FieldType);
    }

    // ── CMC-S003: LoadZoneIntent field types ──────────────────────────────

    [Fact]
    public void LoadZoneIntent_HasRequestIdAndZoneIdFields()
    {
        var fields = typeof(LoadZoneIntent)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(f => f.Name, f => f.FieldType);

        Assert.True(fields.TryGetValue("RequestId", out var reqIdType));
        Assert.Equal(typeof(Guid), reqIdType);

        Assert.True(fields.TryGetValue("ZoneId", out var zoneIdType));
        Assert.Equal(typeof(string), zoneIdType);
    }

    // ── CMC-S003: EventId values 9050–9057 are unique ─────────────────────

    [Fact]
    public void IntentStructs_EventIds9050To9057_AreUniqueAndInRange()
    {
        var intentTypes = new Type[]
        {
            typeof(TransitionStateIntent),
            typeof(ManageEpisodeIntent),
            typeof(SeekReplayIntent),
            typeof(CancelOperationIntent),
            typeof(ExecuteStorageOpIntent),
            typeof(StorageOpCompletedEvent),
            typeof(TakeCheckpointIntent),
            typeof(LoadZoneIntent),
        };

        var ids = intentTypes
            .Select(t => t.GetCustomAttribute<EventIdAttribute>())
            .Select(a => { Assert.NotNull(a); return a!.Id; })
            .ToList();

        // All in 9050-9057 range
        foreach (var id in ids)
            Assert.InRange(id, 9050, 9057);

        // All unique
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // ── TASK-D03: ClusterStateTransitionedEvent.NewStateId is ClusterState ─

    [Fact]
    public void ClusterStateTransitionedEvent_NewStateId_IsClusterStateEnum()
    {
        var ev = new ClusterStateTransitionedEvent { NewStateId = ClusterState.OperatingLive, SubsystemName = "Cluster" };
        Assert.Equal(ClusterState.OperatingLive, ev.NewStateId);

        var field = typeof(ClusterStateTransitionedEvent)
            .GetField("NewStateId", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(ClusterState), field!.FieldType);
    }
}
