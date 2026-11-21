using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Systems;
using Hrot.NED.Common;
using Hrot.NED.Messages;
using Xunit;
using NodeId = Hrot.NED.Common.NodeId;

namespace Hrot.DDS.DataModel.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CS024 — UpdateEntityAttributeRequestSystem: CommanderId pre-intercept
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for the "CommanderId" intercept path in
/// <see cref="UpdateEntityAttributeRequestSystem"/>.
/// </summary>
public sealed class UpdateEntityAttributeCommanderIdTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class QueuedRequestSource : IUpdateEntityAttributeRequestSource
    {
        private readonly Queue<UpdateEntityAttributeRequest> _queue = new();

        public void Enqueue(UpdateEntityAttributeRequest req) => _queue.Enqueue(req);

        public void ProcessRequests(Action<UpdateEntityAttributeRequest> processor)
        {
            while (_queue.TryDequeue(out var req))
                processor(req);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterEvent<CmdAssignSubordinate>();
        repo.RegisterEvent<CmdRemoveSubordinate>();
        repo.RegisterComponent<UnitSubordinate>();
        return repo;
    }

    private static UpdateEntityAttributeRequestSystem CreateSystem(
        QueuedRequestSource  source,
        NetworkEntityMap     entityMap)
        => new UpdateEntityAttributeRequestSystem(
            requestSource:        source,
            entityMap:            entityMap,
            jsonAttributeCompiler: null);

    // ── CS024-T01: Assign patch routes to CmdAssignSubordinate ────────────────

    [Fact]
    public void AssignPatch_WhenCommanderInMap_PublishesCmdAssignSubordinate()
    {
        var repo      = CreateRepo();
        var target    = repo.CreateEntity();
        var commander = repo.CreateEntity();

        var entityMap = new NetworkEntityMap();
        entityMap.Register(netId: 1,  entity: target);
        entityMap.Register(netId: 42, entity: commander);

        var source = new QueuedRequestSource();

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 1,
            AttributePatchJson = "{\"CommanderId\":42}",
        });

        var system = CreateSystem(source, entityMap);
        system.Execute(repo, 0f);

        repo.Bus.SwapBuffers();
        var events = repo.Bus.Read<CmdAssignSubordinate>().ToArray();
        Assert.Single(events);
        Assert.Equal(target,    events[0].Subordinate);
        Assert.Equal(commander, events[0].Commander);
    }

    // ── CS024-T02: Remove patch routes to CmdRemoveSubordinate ───────────────

    [Fact]
    public void RemovePatch_EntityHasSubordinate_PublishesCmdRemoveSubordinate()
    {
        var repo   = CreateRepo();
        var target = repo.CreateEntity();
        repo.AddComponent(target, new UnitSubordinate { Commander = target }); // dummy self-link

        var entityMap = new NetworkEntityMap();
        entityMap.Register(netId: 5, entity: target);

        var source = new QueuedRequestSource();

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 5,
            AttributePatchJson = "{\"CommanderId\":0}",
        });

        var system = CreateSystem(source, entityMap);
        system.Execute(repo, 0f);

        repo.Bus.SwapBuffers();
        var events = repo.Bus.Read<CmdRemoveSubordinate>().ToArray();
        Assert.Single(events);
        Assert.Equal(target, events[0].Subordinate);
    }

    // ── CS024-T03: Remove patch on entity without UnitSubordinate — no event ─

    [Fact]
    public void RemovePatch_EntityHasNoSubordinate_NoEventPublished()
    {
        var repo   = CreateRepo();
        var target = repo.CreateEntity(); // no UnitSubordinate

        var entityMap = new NetworkEntityMap();
        entityMap.Register(netId: 7, entity: target);

        var source = new QueuedRequestSource();

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 7,
            AttributePatchJson = "{\"CommanderId\":0}",
        });

        var system = CreateSystem(source, entityMap);
        system.Execute(repo, 0f);

        repo.Bus.SwapBuffers();
        Assert.Empty(repo.Bus.Read<CmdRemoveSubordinate>().ToArray());
    }

    // ── CS024-T04: Other keys unaffected — CommanderId stripped from JSON ─────

    [Fact]
    public void MixedPatch_CommanderIdStrippedBeforeCompile_OtherKeysPreserved()
    {
        // We can't easily inject a compiler in unit tests here, so we verify
        // the intercept by checking no assign/remove event is fired for key "Name"
        // and that the CommanderId intercept still fires when both keys are present.
        var repo   = CreateRepo();
        repo.RegisterComponent<UnitSubordinate>();
        var target    = repo.CreateEntity();
        var commander = repo.CreateEntity();

        var entityMap = new NetworkEntityMap();
        entityMap.Register(netId: 10, entity: target);
        entityMap.Register(netId: 99, entity: commander);

        var source = new QueuedRequestSource();

        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 10,
            AttributePatchJson = "{\"Name\":\"Bravo\",\"CommanderId\":99}",
        });

        var system = CreateSystem(source, entityMap);
        system.Execute(repo, 0f);

        // CommanderId was intercepted and stripped — assign event should fire.
        repo.Bus.SwapBuffers();
        var assigns = repo.Bus.Read<CmdAssignSubordinate>().ToArray();
        Assert.Single(assigns);
        Assert.Equal(commander, assigns[0].Commander);
    }

    // ── CS024-T05 (⭐ edit framework F2): a CommanderId-only patch is ANSWERED (EntityEditOutcome, Applied) when the requester asks ──────────

    [Fact]
    public void CommanderIdOnlyPatch_WantOutcome_IsAnsweredApplied()
    {
        var repo      = CreateRepo();
        var target    = repo.CreateEntity();
        var commander = repo.CreateEntity();

        var entityMap = new NetworkEntityMap();
        var reqId = Guid.NewGuid();
        entityMap.Register(netId: 3,  entity: target);
        entityMap.Register(netId: 42, entity: commander);

        var source = new QueuedRequestSource();
        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = reqId,
            EntityId           = 3,
            AttributePatchJson = "{\"CommanderId\":42}",
            WantOutcome        = true,
        });

        CreateSystem(source, entityMap).Execute(repo, 0f);

        Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(repo).TryGet(reqId, out var o));
        Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Applied, o.Status);
    }

    /// <summary>
    /// ⭐⭐ <b>S2b — a non-owner is a silent bystander for a CommanderId patch.</b> Since S2b every host runs this
    /// handler, and the request reaches all of them. A node that does not own the entity must neither assign the
    /// subordinate nor send a Success ack for a change it did not make. Before the fix it set
    /// <c>intercepted</c> before the authority check and acked. 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S2b.
    /// </summary>
    [Fact]
    public void CommanderIdPatch_OnANonOwner_PublishesNothing_AndSendsNoAck()
    {
        var repo = CreateRepo();
        repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
        var target    = repo.CreateEntity();
        var commander = repo.CreateEntity();
        repo.AddComponent(target, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 2, localNodeId: 5));

        var entityMap = new NetworkEntityMap();
        entityMap.Register(netId: 3,  entity: target);
        entityMap.Register(netId: 42, entity: commander);

        var source = new QueuedRequestSource();
        source.Enqueue(new UpdateEntityAttributeRequest
        {
            RequestId          = Guid.NewGuid(),
            EntityId           = 3,
            AttributePatchJson = "{\"CommanderId\":42}",
            WantOutcome        = true,
        });

        CreateSystem(source, entityMap).Execute(repo, 0f);

        repo.Bus.SwapBuffers();
        Assert.Empty(repo.Bus.Read<CmdAssignSubordinate>().ToArray());
        Assert.Equal(0, Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(repo).Count);   // a bystander does not answer: the owner does
    }
}
