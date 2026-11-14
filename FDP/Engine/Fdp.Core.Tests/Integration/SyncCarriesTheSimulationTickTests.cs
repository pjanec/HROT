using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Core.Tests.Integration;

/// <summary>
/// ⭐⭐⭐ <c>QA-030</c> — <see cref="EntityRepository.SyncFrom"/> must carry <b>both</b> version clocks.
///
/// <para>📌 <b>The defect these rails pin.</b> <c>SyncFrom</c> copied <c>_globalVersion</c> and left
/// <c>_simulationTick</c> at its construction value of 1. <c>ISimulationView.Tick</c> reads
/// <c>_simulationTick</c>, so every SoD / background snapshot handed to a module system reported
/// <c>Tick == 1</c> for the life of the process while <see cref="EntityRepository.GlobalVersion"/>
/// tracked the live world correctly.</para>
///
/// <para>⛔ <b>Why nothing caught it.</b> The class invariant is
/// <c>_globalVersion &gt;= _simulationTick</c> — advancing one clock and not the other keeps that
/// inequality TRUE, so no assert fired. The failure surfaced three layers away, as an EQS sensor that
/// could never leave <c>_AwaitingRaycasts</c> (the skip-guard compares against the view's tick).</para>
///
/// <para>⚠ <b>These rails assert the TICK, not the version</b> — a rail written against
/// <c>GlobalVersion</c> would have passed throughout the defect's whole life.</para>
/// </summary>
public sealed class SyncCarriesTheSimulationTickTests
{
    /// <summary>
    /// The headline rail: after a sync, the destination's <see cref="ISimulationView.Tick"/> equals the
    /// source's — not the value a fresh repository is constructed with.
    /// </summary>
    [Fact]
    public void SyncFrom_CarriesTheSourcesSimulationTick()
    {
        using var live     = new EntityRepository();
        using var snapshot = new EntityRepository();

        // Precondition: a fresh repository starts at 1, so an un-synced clock is indistinguishable
        // from a correctly-synced one at tick 1. Advance well past it before asserting.
        Assert.Equal(1u, ((ISimulationView)snapshot).Tick);

        for (int i = 0; i < 25; i++) live.Tick();
        uint liveTick = live.SimulationTick;
        Assert.True(liveTick > 1u, "the live repo must have advanced past the construction value");

        snapshot.SyncFrom(live);

        Assert.Equal(liveTick, snapshot.SimulationTick);
        Assert.Equal(liveTick, ((ISimulationView)snapshot).Tick);
    }

    /// <summary>
    /// A snapshot reused across frames — the pooled shape <c>SnapshotPool</c> / <c>OnDemandProvider</c>
    /// use — must see the tick MOVE between syncs. ⭐ This is the property the EQS cross-tick phase
    /// machine depends on: "submitted at tick N, poll on a later tick" is unobservable if every sync
    /// reports the same number.
    /// </summary>
    [Fact]
    public void RepeatedSyncs_AdvanceTheViewTick()
    {
        using var live     = new EntityRepository();
        using var snapshot = new EntityRepository();

        for (int i = 0; i < 5; i++) live.Tick();
        snapshot.SyncFrom(live);
        uint first = ((ISimulationView)snapshot).Tick;

        for (int i = 0; i < 5; i++) live.Tick();
        snapshot.SyncFrom(live);
        uint second = ((ISimulationView)snapshot).Tick;

        Assert.True(second > first,
            $"a re-synced snapshot must observe the advanced tick (first={first}, second={second}).");
    }

    /// <summary>
    /// The invariant that let the defect hide stays intact after the fix:
    /// <c>GlobalVersion &gt;= SimulationTick</c> on the destination.
    /// </summary>
    [Fact]
    public void SyncFrom_PreservesTheVersionInvariant()
    {
        using var live     = new EntityRepository();
        using var snapshot = new EntityRepository();

        for (int i = 0; i < 9; i++) live.Tick();
        live.BumpMemoryVersion();   // global version alone moves ahead — the legal skew
        snapshot.SyncFrom(live);

        Assert.True(snapshot.GlobalVersion >= snapshot.SimulationTick,
            $"invariant violated: GlobalVersion={snapshot.GlobalVersion} < SimulationTick={snapshot.SimulationTick}");
        Assert.Equal(live.SimulationTick, snapshot.SimulationTick);
        Assert.Equal(live.GlobalVersion,  snapshot.GlobalVersion);
    }
}
