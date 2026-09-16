using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-512</c> — a lifecycle breakpoint identified by <c>NetworkId</c> FIRES instead of
/// throwing.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §8a *(the open decision)*.
///
/// <para>⛔⛔ <b>Before this, the arm threw <c>NotSupportedException</c></b> — from inside
/// <c>EvaluateLifecycleTrackers</c>, i.e. <b>inside the tick loop</b>, so authoring one of these did not
/// merely fail to work: it took the frame down. ⚠ Its own comment prescribed injecting an
/// <c>INetworkEntityMap</c>; 📐 <b>that interface does not exist</b> — the name appears only in that
/// comment.</para>
///
/// <para>⭐⭐ <b>Why the fix is neither option the design offered</b> *(the resolver scan or the
/// <c>NetworkEntityMap</c> index)*: this predicate asks *"is THIS entity the one with id N?"*, not
/// *"which entity has id N?"* — 📐 and it is called once per active entity per tracker per tick, so a
/// lookup of either kind would be quadratic. ⭐ The entity's own component answers in O(1).</para>
/// </summary>
[Collection("ComponentRegistry")]
public sealed class TheNetworkIdPredicateAnswersTests
{
    private const long WantedId = 4242;

    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(
            repo, preTick, provider, new MockDebugTimeController(), compiler, eventCompiler);
        return (manager, new DataBreakpointSystem(manager), repo);
    }

    private static Breakpoint NetworkIdBreakpoint(string targetValue) => new()
    {
        Id                  = BreakpointId.Invalid,
        Enabled             = true,
        OccurrenceThreshold = 1,
        DisplayName         = "LifecycleNetworkId",
        Condition           = new LifecyclePredicateDto
        {
            IdentifierType = EntityIdentifierType.NetworkId,
            TargetValue    = targetValue,
        },
    };

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL:</b> the tracked entity is the one carrying <see cref="WantedId"/> — and the
    /// decoy beside it, which carries a different id, does NOT fire.
    /// <para>⚠ The decoy is the half that matters: an arm that matched everything would also pass a
    /// single-entity rail.</para>
    /// </summary>
    [Fact]
    public void ALifecycleBreakpointOnANetworkIdFiresForThatEntityOnly()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<NetworkIdentity>();

        var wanted = repo.CreateEntity();
        repo.SetComponent(wanted, new NetworkIdentity(WantedId));
        var decoy = repo.CreateEntity();
        repo.SetComponent(decoy, new NetworkIdentity(WantedId + 1));

        int hits = 0;
        Entity lastHit = default;
        manager.OnBreakpointHit += (_, e) => { hits++; lastHit = e; };

        manager.Add(NetworkIdBreakpoint(WantedId.ToString()));

        system.Execute(repo, 0f);

        Assert.Equal(1, hits);
        Assert.Equal(wanted, lastHit);
    }

    /// <summary>
    /// ⛔⛔ <b>A malformed or unmatched target MATCHES NOTHING — it does NOT throw.</b>
    /// 📄 <c>FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md</c>: this code runs inside the tick loop, and
    /// a half-authored breakpoint that throws there killed the editor on every launch. ⭐ Three shapes,
    /// all of which used to be unreachable because the arm threw first.
    /// </summary>
    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("0")]              // ⛔ 0 is "no network identity", never a valid target
    [InlineData("999999")]         // ⚠ well-formed and simply absent
    public void AnUnmatchableNetworkIdTargetFiresNothingAndDoesNotThrow(string target)
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<NetworkIdentity>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new NetworkIdentity(WantedId));

        int hits = 0;
        manager.OnBreakpointHit += (_, _) => hits++;

        manager.Add(NetworkIdBreakpoint(target));

        system.Execute(repo, 0f);      // ⛔ used to throw NotSupportedException from inside the tick

        Assert.Equal(0, hits);
    }

    /// <summary>
    /// ⚠ An entity with <b>no</b> <c>NetworkIdentity</c> at all is simply not a match — ⛔ the arm must
    /// not read a component that is absent.
    /// </summary>
    [Fact]
    public void AnEntityWithoutANetworkIdentityIsNotAMatch()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<NetworkIdentity>();

        repo.CreateEntity();           // ⚠ no NetworkIdentity on it

        int hits = 0;
        manager.OnBreakpointHit += (_, _) => hits++;

        manager.Add(NetworkIdBreakpoint(WantedId.ToString()));

        system.Execute(repo, 0f);

        Assert.Equal(0, hits);
    }
}
