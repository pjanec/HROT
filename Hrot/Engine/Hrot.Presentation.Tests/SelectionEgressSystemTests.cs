using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Systems;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-6</c> — <see cref="SelectionEgressSystem"/>: what this host tells
/// remote observers, and what it deliberately does NOT.</b>
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.6 (the carve-out) · §2.7.17 (the as-built).
/// </summary>
public sealed class SelectionEgressSystemTests
{
    private readonly EntityRepository _world;
    private readonly SelectionRequestSystem _requests;
    private readonly List<IReadOnlyList<int>> _sent = new();
    private readonly SelectionEgressSystem _egress;
    private long _nextNetId = 8100L;

    public SelectionEgressSystemTests()
    {
        _world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_world);
        _world.RegisterComponent<SelectionState>();
        _world.RegisterComponent<VehicleState>();
        _requests = new SelectionRequestSystem(
            () => new Hrot.ScenarioEditor.Selection.EcsSelectionState(_world));
        _egress = new SelectionEgressSystem(ids => _sent.Add(ids));
    }

    private Entity CreateEntity()
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, default(SimTransform));
        _world.AddComponent(e, new NetworkIdentity { Value = _nextNetId++ });
        _world.AddComponent(e, new SelectionState());
        return e;
    }

    /// <summary>Request → the one writer applies AND announces → the egress reads the announcement.</summary>
    private void Serve(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest req)
    {
        _world.Bus.PublishManaged(req);
        _world.Bus.SwapBuffers();
        _requests.Execute(_world, 0f);
        _world.Bus.SwapBuffers();
        _egress.Execute(_world, 0f);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE GAP THIS SLICE EXISTS TO CLOSE.</b> 🔴 The outbound event used to be published from
    /// inside IG's MAP-CLICK handler, so a selection changed by the entity inspector, the orbat, a
    /// context-menu <i>Select</i> or a remote command <b>never reached ExCon at all</b> — its panel kept
    /// painting a selection this host no longer had.
    ///
    /// <para>⭐ Driving it off the ANNOUNCEMENT means every cause propagates, by construction. ⛔
    /// Red-proof: hang the publish off one cause again and this reddens for every other one.</para>
    /// </summary>
    [Fact]
    public void ASelectionChangeFromAnyLocalCause_ReachesTheObservers()
    {
        var a = CreateEntity();

        Serve(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(a, "Inspector.RightClick"));     // ⭐ NOT a map gesture

        var sent = Assert.Single(_sent);
        Assert.Equal(new[] { (int)_world.GetComponent<NetworkIdentity>(a).Value }, sent);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>ECHO SUPPRESSION, AT THE EGRESS — §2.6's ruling, and the reason this slice exists.</b>
    /// 🔒 <i>"Echo suppression belongs at the EGRESS translator (do not re-publish outward what ingress
    /// produced), never by muting the internal notification."</i>
    ///
    /// <para>⛔ <b>The second assertion is the whole point and is easy to lose:</b> the notification is
    /// still published, so every LOCAL surface follows a remote selection. Only the outbound hop is
    /// skipped. ⚠ The old implementation achieved suppression by not notifying, which would leave the
    /// local inspector and every panel stale — that is what §2.6 forbids by name.</para>
    /// </summary>
    [Fact]
    public void ARemoteOriginatedChange_IsNotEchoedBack_ButStillLandsLocally()
    {
        var a = CreateEntity();

        Serve(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(a, SelectionEgressSystem.RemoteOriginPrefix + "SetSelection"));

        Assert.Empty(_sent);                                              // ⭐ no echo
        Assert.True(_world.GetComponent<SelectionState>(a).IsSelected);   // ⭐ and it DID apply locally
    }

    /// <summary>
    /// ⭐⭐ <b>A CLEAR IS A MESSAGE.</b> ⛔ Skipping an empty set would leave every observer painting a
    /// selection that no longer exists — the same "accepted and silently discarded" shape that bit
    /// <c>SelectionRequestSystem</c>'s own <c>Clear</c> branch (§2.7.7).
    /// </summary>
    [Fact]
    public void ClearingTheSelection_IsSentAsAnEmptySet_NotSkipped()
    {
        var a = CreateEntity();
        Serve(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ReplaceWith(a, "Map.Click"));
        _sent.Clear();

        Serve(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ClearAll("Map.RightClick.EmptySpace"));

        var sent = Assert.Single(_sent);
        Assert.Empty(sent);
    }

    /// <summary>
    /// ⚠ An entity with no usable network id is SKIPPED, not sent as <c>0</c>. ⛔ Zero is the
    /// "unreplicated" sentinel (§6.8's <i>"no id, no pick target"</i>), and a peer resolving it would
    /// select whatever happened to answer.
    /// </summary>
    [Fact]
    public void AnEntityWithNoNetworkId_IsNotSentAsZero()
    {
        var real = CreateEntity();
        var local = _world.CreateEntity();               // ⛔ no NetworkIdentity at all
        _world.AddComponent(local, default(SimTransform));
        _world.AddComponent(local, new SelectionState());

        Serve(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(new[] { real, local }, "Map.RubberBand"));

        var sent = Assert.Single(_sent);
        Assert.Equal(new[] { (int)_world.GetComponent<NetworkIdentity>(real).Value }, sent);
        Assert.DoesNotContain(0, sent);
    }

    /// <summary>
    /// ⭐ The predicate itself, asserted directly rather than inferred from a publish that did not
    /// happen — ⛔ an absence is the weakest possible assertion, and this is the one line that decides
    /// whether a remote command is echoed.
    /// </summary>
    [Theory]
    [InlineData("Remote.SetSelection", true)]
    [InlineData("Map.Click", false)]
    [InlineData("Inspector.RightClick", false)]
    [InlineData(null, false)]
    public void IsRemoteOrigin_KeysOnThePrefix(string? reason, bool expected)
        => Assert.Equal(expected, SelectionEgressSystem.IsRemoteOrigin(reason));
}
