using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IG.Components;
using Bagira.Map.Common.Replication.Utils;
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
    public List<(Guid RequestId, int ErrorCode)> WrittenAcks { get; } = new();
    public void WriteAck(Guid requestId, int errorCode) => WrittenAcks.Add((requestId, errorCode));
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

    // ── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Patching "Name" via <c>AttributePatchJson</c> mutates the live ECS component.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_JsonPatch_PatchesNameOnLiveEntity()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "old" });

        var entityMap = new NetworkEntityMap();
        entityMap.Register(42L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        var requestId = Guid.NewGuid();
        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId         = requestId,
            EntityId          = 42,
            AttributePatchJson = "{\"Name\":\"new\"}",
        });

        // Act
        system.Create(repo);
        system.Run();

        // Assert: IgEntityData.Name is updated in-place.
        var nameData = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("new", nameData.Name);

        // ACK with Success.
        Assert.Single(ackSink.WrittenAcks);
        Assert.Equal(requestId,                      ackSink.WrittenAcks[0].RequestId);
        Assert.Equal((int)SstErrorCode.Success, ackSink.WrittenAcks[0].ErrorCode);
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
    /// the system writes an EntityNotFound ACK without modifying any ECS state.
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
        });

        // Act
        system.Create(repo);
        system.Run();

        // Assert: single ACK with EntityNotFound (=2).
        Assert.Single(ackSink.WrittenAcks);
        Assert.Equal(requestId,                            ackSink.WrittenAcks[0].RequestId);
        Assert.Equal((int)SstErrorCode.EntityNotFound, ackSink.WrittenAcks[0].ErrorCode);
    }

    /// <summary>
    /// An empty JSON object <c>{}</c> produces no mutations and still ACKs with Success.
    /// </summary>
    [Fact]
    public void UpdateEntityAttributeRequestSystem_EmptyJson_AcksSuccess_NoMutation()
    {
        // Arrange
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "unchanged" });

        var entityMap = new NetworkEntityMap();
        entityMap.Register(5L, entity);

        var compiler = BuildCompiler();
        var (system, source, ackSink) = BuildSystem(entityMap, compiler);

        var requestId = Guid.NewGuid();
        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = requestId,
            EntityId           = 5,
            AttributePatchJson = "{}",
        });

        // Act — must not throw.
        system.Create(repo);
        var ex = Record.Exception(() => system.Run());

        // Assert: no exception.
        Assert.Null(ex);

        // Name is unchanged.
        var data = ((ISimulationView)repo).GetManagedComponentRO<IgEntityData>(entity);
        Assert.Equal("unchanged", data.Name);

        // ACK with Success.
        Assert.Single(ackSink.WrittenAcks);
        Assert.Equal(requestId,                      ackSink.WrittenAcks[0].RequestId);
        Assert.Equal((int)SstErrorCode.Success, ackSink.WrittenAcks[0].ErrorCode);
    }
}
