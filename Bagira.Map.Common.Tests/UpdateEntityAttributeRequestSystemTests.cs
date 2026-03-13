using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using FDP.Toolkit.Replication.Patching;
using Bagira.Map.Common.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Tests;

// ─────────────────────────────────────────────────────────────
// Test stubs
// ─────────────────────────────────────────────────────────────

internal sealed class StubUpdateAttrRequestSource : IUpdateEntityAttributeRequestSource
{
    private readonly List<UpdateEntityAttributeRequest> _pending = new();

    public void Enqueue(UpdateEntityAttributeRequest req) => _pending.Add(req);

    public void ProcessRequests(Action<UpdateEntityAttributeRequest> processor)
    {
        foreach (var req in _pending)
            processor(req);
        _pending.Clear();
    }
}

internal sealed class StubUpdateAttrAckSink : IUpdateEntityAttributeAckSink
{
    public List<(Guid RequestId, int ErrorCode, NodeId RespondingNode, byte[] OpaqueData)> WrittenAcks { get; } = new();
    public List<(Guid RequestId, int ErrorCode)> WrittenErrorAcks { get; } = new();

    public void WriteAck(Guid requestId, int errorCode, NodeId respondingNode, ReadOnlySpan<byte> opaqueData)
        => WrittenAcks.Add((requestId, errorCode, respondingNode, opaqueData.ToArray()));

    public void WriteErrorAck(Guid requestId, int errorCode)
        => WrittenErrorAcks.Add((requestId, errorCode));

    /// <summary>Combined view of all ACKs (success + error) as (RequestId, ErrorCode) tuples.</summary>
    public List<(Guid RequestId, int ErrorCode)> AllAcks =>
        WrittenAcks.Select(a => (a.RequestId, a.ErrorCode))
            .Concat(WrittenErrorAcks)
            .ToList();
}

// ─────────────────────────────────────────────────────────────
// ATTR-S5T3 — UpdateEntityAttributeRequestSystem tests
// ─────────────────────────────────────────────────────────────

public class UpdateEntityAttributeRequestSystemTests
{
    private const long EntityInfoOrdinal = (long)EDescriptorType.dtEntityInfo;

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<IgEntityData>();
        return repo;
    }

    /// <summary>
    /// Builds a compiler that registers "Name" and "Affiliation" routes under
    /// <c>dtEntityInfo</c> ordinal, matching the production factory registration.
    /// </summary>
    private static JsonAttributeCompiler BuildCompiler()
    {
        return new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>(
                "Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty,
                descriptorOrdinal: EntityInfoOrdinal)
            .RegisterReferencePath<IgEntityData>(
                "Affiliation",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.ForceId = r.GetString() == "FORCE_FRIENDLY" ? ForceId.Friend : ForceId.Unknown,
                descriptorOrdinal: EntityInfoOrdinal)
            .Build();
    }

    private static (UpdateEntityAttributeRequestSystem system,
                    StubUpdateAttrRequestSource source,
                    StubUpdateAttrAckSink ackSink)
        BuildSystem(NetworkEntityMap entityMap, JsonAttributeCompiler? compiler = null)
    {
        var source  = new StubUpdateAttrRequestSource();
        var ackSink = new StubUpdateAttrAckSink();
        var system  = new UpdateEntityAttributeRequestSystem(source, ackSink, entityMap, compiler);
        return (system, source, ackSink);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Existing tests (updated for authority model)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Patching "Name" via <c>AttributePatchJson</c> mutates the live ECS component
    /// when this node has authority over <c>IgEntityData</c>.
    /// With <c>RequireAck=true</c>, an ACK is returned containing a bitmask that
    /// marks the applied component ID.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_JsonPatch_PatchesNameOnLiveEntity()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "old" });
        // Grant authority so the compiler dispatches the setter.
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var entityMap = new NetworkEntityMap();
        entityMap.Register(42L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        var requestId = Guid.NewGuid();
        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = requestId,
            EntityId           = 42,
            AttributePatchJson = "{\"Name\":\"new\"}",
            RequireAck         = true,
        });

        // Act
        system.Create(repo);
        system.Run();

        // Assert: IgEntityData.Name is updated in-place.
        var nameData = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("new", nameData.Name);

        // ACK sent (RequireAck=true + something applied).
        Assert.Single(ackSink.WrittenAcks);
        Assert.Equal(requestId,                      ackSink.WrittenAcks[0].RequestId);
        Assert.Equal((int)SstErrorCode.Success, ackSink.WrittenAcks[0].ErrorCode);

        // OpaqueData bitmask has the IgEntityData component bit set.
        int compId        = ManagedComponentType<IgEntityData>.ID;
        byte[] mask       = ackSink.WrittenAcks[0].OpaqueData;
        bool compBitIsSet = (mask[compId >> 3] & (1 << (compId & 7))) != 0;
        Assert.True(compBitIsSet, "OpaqueData bitmask should have the IgEntityData component bit set.");

        Assert.Empty(ackSink.WrittenErrorAcks);
    }

    /// <summary>
    /// After a successful patch, the descriptor ordinal for the touched component
    /// type appears in <see cref="EgressPublicationState.DirtyDescriptors"/>.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_JsonPatch_FlushDirtyMarksCalledForEntityInfoOrdinal()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "alpha" });
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var entityMap = new NetworkEntityMap();
        entityMap.Register(10L, entity);

        var compiler = BuildCompiler();
        var (system, source, _) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 10,
            AttributePatchJson = "{\"Name\":\"beta\"}",
        });

        // Act
        system.Create(repo);
        system.Run();

        // Assert: EgressPublicationState carries the dtEntityInfo ordinal.
        var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
        Assert.NotNull(state);
        Assert.Contains(EntityInfoOrdinal, state.DirtyDescriptors);
    }

    /// <summary>
    /// Patching two distinct fields mapped to the same ordinal produces a single
    /// dirty-mark for that ordinal (deduplication via <c>HashSet</c> semantics).
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_DualFieldPatch_BothApplied_SingleDirtyFlush()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "old", ForceId = ForceId.Unknown });
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var entityMap = new NetworkEntityMap();
        entityMap.Register(77L, entity);

        var compiler = BuildCompiler();
        var (system, source, _) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 77,
            AttributePatchJson = "{\"Name\":\"updated\",\"Affiliation\":\"FORCE_FRIENDLY\"}",
        });

        // Act
        system.Create(repo);
        system.Run();

        // Assert: both fields patched.
        var data = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("updated",       data.Name);
        Assert.Equal(ForceId.Friend,  data.ForceId);

        // Ordinal appears exactly once.
        var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
        int count = state.DirtyDescriptors.Count(o => o == EntityInfoOrdinal);
        Assert.Equal(1, count);
    }

    /// <summary>
    /// If the requested entity does not exist in the <see cref="NetworkEntityMap"/>,
    /// the system writes an EntityNotFound error-ACK when <c>RequireAck=true</c>.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_UnknownEntityId_AcksEntityNotFound()
    {
        // Arrange — entity map is empty; entity ID 99 does not exist.
        var repo      = CreateRepo();
        var entityMap = new NetworkEntityMap(); // no registered entities
        var compiler  = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        var requestId = Guid.NewGuid();
        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = requestId,
            EntityId           = 99,
            AttributePatchJson = "{\"Name\":\"ghost\"}",
            RequireAck         = true,
        });

        // Act
        system.Create(repo);
        system.Run();

        // Assert: error ACK with EntityNotFound (=2).
        Assert.Single(ackSink.WrittenErrorAcks);
        Assert.Equal(requestId,                             ackSink.WrittenErrorAcks[0].RequestId);
        Assert.Equal((int)SstErrorCode.EntityNotFound, ackSink.WrittenErrorAcks[0].ErrorCode);
        Assert.Empty(ackSink.WrittenAcks);
    }

    /// <summary>
    /// An empty JSON object <c>{}</c> matches no compiler routes.
    /// No mutation occurs and no ACK is sent (silent bystander rule).
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_EmptyJson_NoMutation_NoAck()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "unchanged" });
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var entityMap = new NetworkEntityMap();
        entityMap.Register(5L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 5,
            AttributePatchJson = "{}",
            RequireAck         = true, // even with RequireAck, silent bystander trumps
        });

        // Act — must not throw.
        system.Create(repo);
        var ex = Record.Exception(() => system.Run());

        // Assert: no exception.
        Assert.Null(ex);

        // Name is unchanged (nothing matched any route).
        var data = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("unchanged", data.Name);

        // No ACK — nothing was applied, silent bystander rule.
        Assert.Empty(ackSink.WrittenAcks);
        Assert.Empty(ackSink.WrittenErrorAcks);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Authority filtering tests
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When this node HAS authority over the component, the setter is dispatched
    /// and the ECS value is mutated.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_Authority_AppliesWhenHasAuthority()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "before" });
        // Grant authority for IgEntityData.
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var entityMap = new NetworkEntityMap();
        entityMap.Register(1L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 1,
            AttributePatchJson = "{\"Name\":\"after\"}",
            RequireAck         = true,
        });

        system.Create(repo);
        system.Run();

        // Mutation applied.
        var data = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("after", data.Name);

        // ACK with success + non-empty bitmask.
        Assert.Single(ackSink.WrittenAcks);
        Assert.Equal((int)SstErrorCode.Success, ackSink.WrittenAcks[0].ErrorCode);
        int  compId = ManagedComponentType<IgEntityData>.ID;
        byte[] mask = ackSink.WrittenAcks[0].OpaqueData;
        Assert.True((mask[compId >> 3] & (1 << (compId & 7))) != 0);
    }

    /// <summary>
    /// When this node does NOT have authority over the component, the setter is skipped
    /// (reader.Skip is called), the ECS value is not mutated, and no ACK is sent
    /// (silent bystander rule).
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_Authority_SkipsWhenNoAuthority()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "untouched" });
        // Authority is NOT set — entity's authority mask bit is 0.

        var entityMap = new NetworkEntityMap();
        entityMap.Register(2L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 2,
            AttributePatchJson = "{\"Name\":\"should-not-apply\"}",
            RequireAck         = true, // won't fire — nothing was applied
        });

        system.Create(repo);
        system.Run();

        // ECS state unchanged — setter was bypassed.
        var data = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("untouched", data.Name);

        // No ACK — silent bystander (nothing applied).
        Assert.Empty(ackSink.WrittenAcks);
        Assert.Empty(ackSink.WrittenErrorAcks);
    }

    /// <summary>
    /// <c>RequireAck=false</c> (fire-and-forget): even when this node successfully applies
    /// a mutation, no ACK is emitted.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_RequireAckFalse_NoAckEvenWhenApplied()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "old" });
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var entityMap = new NetworkEntityMap();
        entityMap.Register(3L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 3,
            AttributePatchJson = "{\"Name\":\"new\"}",
            RequireAck         = false, // fire-and-forget
        });

        system.Create(repo);
        system.Run();

        // Mutation DID happen.
        var data = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("new", data.Name);

        // No ACK — RequireAck=false.
        Assert.Empty(ackSink.WrittenAcks);
        Assert.Empty(ackSink.WrittenErrorAcks);
    }

    /// <summary>
    /// Entity-not-found error is only reported via an error ACK when
    /// <c>RequireAck=false</c>: no ACK, no crash.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_UnknownEntityId_NoAckWhenRequireAckFalse()
    {
        // Arrange
        var repo      = CreateRepo();
        var entityMap = new NetworkEntityMap();
        var compiler  = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 999,
            AttributePatchJson = "{\"Name\":\"ghost\"}",
            RequireAck         = false,
        });

        system.Create(repo);
        var ex = Record.Exception(() => system.Run());

        Assert.Null(ex);
        Assert.Empty(ackSink.WrittenAcks);
        Assert.Empty(ackSink.WrittenErrorAcks);
    }
}



