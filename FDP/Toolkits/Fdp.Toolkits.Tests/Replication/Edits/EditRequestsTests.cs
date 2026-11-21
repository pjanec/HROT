using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Events;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication.Edits;

/// <summary>
/// ⭐ CE-2380 part 1 (R-2082, 📄 docs/DESIGN_Remote_Edit_Outcome.md §2/§4 A,D,E,F) — the requester's side of a remote edit: <see cref="EditRequests"/> routes through the EXISTING write
/// path, reports at once what this node can know (Direct ⇒ Applied, Refused ⇒ Refused + reason), and a Requested edit publishes the command carrying its id and <c>WantOutcome</c>.
/// </summary>
public sealed class EditRequestsTests
{
    private const int NetId = 90_210;

    private static EntityRepository World(out Entity e, bool owns, bool replicated = true)
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterManagedEvent<UpdateEntityAttributeCommand>();
        e = repo.CreateEntity();
        repo.SetComponent(e, new SimTransform { Rotation = Quaternion.Identity });
        if (replicated) repo.AddComponent(e, new NetworkIdentity { Value = NetId });
        repo.SetAuthority<SimTransform>(e, owns);
        return repo;
    }

    private static EntityAttributeChange[] Heading90 => new[] { EntityAttributeChange.Double(AttributeIds.Heading, 90.0) };

    /// <summary>Lean F: an owned edit is Applied at once, in the log and on the bus. ✅ Red-proof: the Direct arm of ReportRoute removed ⇒ red.</summary>
    [Fact]
    public void ADirectEdit_IsReportedAppliedAtOnce()
    {
        using var repo = World(out var e, owns: true);

        var ticket = EditRequests.Write(repo, e, Heading90);

        Assert.Equal(EntityWriteRoute.Direct, ticket.Route);
        Assert.NotEqual(Guid.Empty, ticket.RequestId);
        Assert.True(EditOutcomeLog.Of(repo).TryGet(ticket.RequestId, out var o));
        Assert.Equal((EditStatus.Applied, NetId, false), (o.Status, o.NetworkId, o.FromNetwork));
        repo.Bus.SwapBuffers();
        Assert.Single(repo.Bus.ReadManaged<EditOutcome>().Where(x => x.RequestId == ticket.RequestId));
    }

    /// <summary>An unreplicated entity this node does not own: nobody to ask ⇒ Refused with that reason. ✅ Red-proof: the post-route netId check removed ⇒ route stays Requested ⇒ red.</summary>
    [Fact]
    public void AnEditNobodyCanBeAskedAbout_IsReportedRefused_WithTheReason()
    {
        using var repo = World(out var e, owns: false, replicated: false);

        var ticket = EditRequests.Write(repo, e, Heading90);

        Assert.Equal(EntityWriteRoute.Refused, ticket.Route);
        Assert.True(EditOutcomeLog.Of(repo).TryGet(ticket.RequestId, out var o));
        Assert.Equal(EditStatus.Refused, o.Status);
        Assert.Contains("not replicated", o.Reason);
        repo.Bus.SwapBuffers();
        Assert.Empty(repo.Bus.ReadManaged<UpdateEntityAttributeCommand>());   // nothing went out
    }

    /// <summary>A Requested edit: the command carries the minted id and WantOutcome, and NO local outcome is invented (the owner answers). ✅ Red-proof: PublishOnto not stamping the id ⇒ red.</summary>
    [Fact]
    public void ARequestedEdit_PublishesTheCommandWithItsIdAndWantOutcome()
    {
        using var repo = World(out var e, owns: false);

        var ticket = EditRequests.Write(repo, e, Heading90);

        Assert.Equal(EntityWriteRoute.Requested, ticket.Route);
        repo.Bus.SwapBuffers();
        var cmd = Assert.Single(repo.Bus.ReadManaged<UpdateEntityAttributeCommand>());
        Assert.Equal((ticket.RequestId, true, NetId), (cmd.RequestId, cmd.WantOutcome, cmd.NetworkId));
        Assert.False(EditOutcomeLog.Of(repo).TryGet(ticket.RequestId, out _));
    }

    /// <summary>The JSON arm: the same — id and WantOutcome on the command; an unreplicated entity is refused with the reason.</summary>
    [Fact]
    public void TheJsonArm_CarriesTheIdAndWantOutcome_OrIsRefused()
    {
        using var repo = World(out var e, owns: false);
        var ticket = EditRequests.WriteJson(repo, e, "{\"Heading\":90}");
        Assert.Equal(EntityWriteRoute.Requested, ticket.Route);
        repo.Bus.SwapBuffers();
        var cmd = Assert.Single(repo.Bus.ReadManaged<UpdateEntityAttributeCommand>());
        Assert.Equal((ticket.RequestId, true, "{\"Heading\":90}"), (cmd.RequestId, cmd.WantOutcome, cmd.AttributePatchJson));

        using var bare = World(out var e2, owns: false, replicated: false);
        var refused = EditRequests.WriteJson(bare, e2, "{\"Heading\":90}");
        Assert.Equal(EntityWriteRoute.Refused, refused.Route);
        Assert.True(EditOutcomeLog.Of(bare).TryGet(refused.RequestId, out var o));
        Assert.Contains("not replicated", o.Reason);
    }

    /// <summary>The existing publisher is unchanged for callers that do not ask: Guid.Empty and WantOutcome false. ✅ Red-proof: the one-argument overload passing true ⇒ red.</summary>
    [Fact]
    public void TheExistingPublisher_StillSendsNoIdAndNoOutcomeRequest()
    {
        using var repo = World(out var e, owns: false);
        EntityAttributeChangeRequests.PublishOnto(repo)(e, Heading90);
        EntityAttributeChangeRequests.TryPublishJsonPatch(repo, e, "{\"Heading\":1}");
        repo.Bus.SwapBuffers();
        var cmds = repo.Bus.ReadManaged<UpdateEntityAttributeCommand>().ToArray();
        Assert.Equal(2, cmds.Length);
        Assert.All(cmds, c => Assert.Equal((Guid.Empty, false), (c.RequestId, c.WantOutcome)));
    }

    /// <summary>The log answers the LATEST outcome of a request (Pending then Refused ⇒ Refused), is a ring of 64, and an empty id reports nothing. ✅ Red-proof: TryGet scanning oldest-first ⇒ red.</summary>
    [Fact]
    public void TheLog_ReturnsTheLatestPerRequest_IsARing_AndEmptyIdsAreNoOps()
    {
        using var repo = new EntityRepository();
        var id = Guid.NewGuid();
        EditOutcomes.Report(repo, id, 1, EditStatus.Pending, "judging", respondingNode: 7);
        EditOutcomes.Report(repo, id, 1, EditStatus.Refused, "no", respondingNode: 7);
        Assert.True(EditOutcomeLog.Of(repo).TryGet(id, out var latest));
        Assert.Equal(EditStatus.Refused, latest.Status);

        Assert.Null(EditOutcomes.Report(repo, Guid.Empty, 1, EditStatus.Applied, "x"));
        Assert.False(EditOutcomeLog.Of(repo).TryGet(Guid.Empty, out _));

        for (int i = 0; i < EditOutcomeLog.Capacity; i++) EditOutcomes.Report(repo, Guid.NewGuid(), i, EditStatus.Applied, "", respondingNode: 7);
        Assert.Equal(EditOutcomeLog.Capacity, EditOutcomeLog.Of(repo).Count);
        Assert.False(EditOutcomeLog.Of(repo).TryGet(id, out _));   // pushed out of the ring
        Assert.Same(EditOutcomeLog.Of(repo), EditOutcomeLog.Of(repo));
    }

    /// <summary>Lean E: a reason is capped at 1000 characters by the factory (and reaches the log capped). ✅ Red-proof: Cap returning the input ⇒ red.</summary>
    [Fact]
    public void AReason_IsCappedAt1000()
    {
        using var repo = new EntityRepository();
        var id = Guid.NewGuid();
        EditOutcomes.Report(repo, id, 1, EditStatus.Refused, new string('x', 5000), respondingNode: 7);
        Assert.True(EditOutcomeLog.Of(repo).TryGet(id, out var o));
        Assert.Equal(EditOutcome.MaxReasonLength, o.Reason.Length);
        Assert.Equal(string.Empty, EditOutcome.Create(id, 1, EditStatus.Applied, null, 1, 0, false).Reason);
    }
}
