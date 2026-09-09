using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// 🔴🔴 <b><c>HN-037</c> — THE GUARD RAIL.</b> 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11b/§11c ·
/// handoff §3 *("prove the guard with a revert-goes-red rail")*.
///
/// <para>⭐⭐ <b>What is actually at risk here, stated once.</b> Resetting the id authority BACKWARD to 1000 is
/// what makes the authored block reproducible and cross-host equal — and it is only safe because the world
/// has just been cleared. ⛔ The same call mid-exercise fights <c>mgmt-1</c> §5.7's FORWARD high-water mark
/// and flushes pools other nodes are mid-way through, which would hand two live entities the same id. ⇒ the
/// dangerous property is not <i>"does it fire?"</i> but <b><i>"does it fire ANYWHERE ELSE?"</i></b></para>
///
/// <para>⚠⚠ <b>That is why this file spends more cases on NOT firing than on firing.</b> A rail that only
/// asserted the reset happens would stay green if the guard were deleted entirely — the exact vacuity that
/// bit the gizmo batch. <c>WorldIdResetCount</c> exists so non-firing is observable at all.</para>
/// </summary>
[Collection("OrchestratorTests")]
public sealed class TheWorldBoundaryResetsTheIdAuthorityTests
{
    /// <summary>Records what the master asked of the authority, so both firing AND silence are checkable.</summary>
    private sealed class RecordingAuthority : IWorldIdAuthority
    {
        public List<long> Resets { get; } = new();
        public void ResetToBase(long firstId) => Resets.Add(firstId);
    }

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    private static void RegisterNode(FdpEventBus bus, ClusterMaster master)
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    private static void RequestTransition(FdpEventBus bus, ClusterMaster master, ClusterState target)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)target).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    private static (FdpEventBus Bus, ClusterMaster Master, RecordingAuthority Authority) Cluster()
    {
        var bus       = new FdpEventBus();
        var master    = new ClusterMaster(bus, NoMandatoryConfig());
        var authority = new RecordingAuthority();
        master.IdAuthority = authority;
        RegisterNode(bus, master);
        return (bus, master, authority);
    }

    // ⚠ The cluster starts in Idle, and a BFS path from Idle to a Loading* state is what the guard reads.

    // ── ① It fires at the world boundary, with the right value ───────────────────────────────

    /// <summary>
    /// ⭐⭐ A live scenario load resets the authority to <b>1000</b> — the user's number, and the one that
    /// makes editor and <c>--mode all</c> agree by construction.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_live_scenario_load_resets_the_authority_to_1000()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingLive);

        Assert.Equal(new[] { WorldIdAuthority.WorldBase }, authority.Resets);
    }

    /// <summary>
    /// ⭐⭐ And an EDIT load does too. 🔒 User, `2026-08-24`: <i>"Editor is no exception."</i>
    /// <para>📌 The editor reaches this through the very same class — it runs its own <c>ClusterMaster</c>
    /// (<c>EditorSubsystem.cs</c>, "the editor is a ONE-NODE cluster"), which is why the unification is one
    /// hook and not two.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void An_edit_load_resets_the_authority_too()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingEdit);

        Assert.Equal(new[] { WorldIdAuthority.WorldBase }, authority.Resets);
    }

    /// <summary>
    /// ⭐ The trajectory is what is inspected, not the requested target: <c>Idle → OperatingLive</c> is a BFS
    /// path that passes THROUGH <c>LoadingLive</c>. ⚠ Keying on the target alone would silently never fire —
    /// a green build with the whole feature inert.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void The_boundary_is_detected_on_a_pass_through_state_not_only_the_target()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        // OperatingLive is NOT itself a boundary state; LoadingLive on the way to it is.
        RequestTransition(bus, master, ClusterState.OperatingLive);

        Assert.Single(authority.Resets);
    }

    // ── ② 🔴 It fires NOWHERE ELSE — the half that carries the safety argument ────────────────

    /// <summary>
    /// 🔴🔴 <b>Replay must NOT reset to 1000.</b> §11c: replay's policy is a reset FORWARD to the recording's
    /// high-water mark, over a world that is PRE-POPULATED. ⛔ A backward reset to a constant here would
    /// re-issue ids the recording's own entities already hold.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Replay_does_not_reset_the_authority()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingReplay);

        Assert.Empty(authority.Resets);
    }

    /// <summary>
    /// 🔴🔴 <b>Preview must NOT touch the central authority.</b> §4d: preview does NOT clear the world, so
    /// each node restores its OWN pool locally. ⛔ Resetting the master here would hand live entities' ids
    /// out again — and preview is the case the whole <c>IRestorableIdAllocator</c> seam exists for.
    ///
    /// <para>⚠⚠ <b>Entered from <c>OperatingEdit</c>, deliberately — and the first version of this case was
    /// WRONG.</b> It asked for <c>Idle → OperatingPreview</c> and failed, because that BFS path is
    /// <c>Idle → LoadingEdit → OperatingEdit → LoadingPreview → OperatingPreview</c>: it drags a real edit
    /// LOAD along, and resetting there is correct. ⭐ The preview that must not reset is the one a user
    /// actually takes — from a world already loaded. 📌 The rail was fixed to ask the real question rather
    /// than the guard being loosened to pass the wrong one.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Preview_from_a_loaded_edit_session_does_not_reset_the_authority()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingEdit);   // the world IS created here
        authority.Resets.Clear();

        RequestTransition(bus, master, ClusterState.OperatingPreview);

        Assert.Empty(authority.Resets);
    }

    /// <summary>
    /// 🔴🔴🔴 <b>THE LIVE-FROM-REPLAY BRANCH — a <c>LoadingLive</c> step that is NOT a world boundary.</b>
    /// 📐 Found by this rail, `2026-08-24`; §11c's three-policy table does not enumerate it.
    ///
    /// <para>The graph carries <c>OperatingReplay → LoadingLive</c> *(<c>CGF1-S0305</c>)*. On that path
    /// <c>ReferenceReplayLoadHandler</c> claims <c>PrepareLive</c> because a replay session is active, so
    /// <c>CgfScenarioLoadHandler</c> never runs and nothing is extracted from a file — ⛔ <b>the world is not
    /// cleared, it CONTINUES from the replayed state</b>, with entities already holding ids in the
    /// 1000-block. ⇒ a reset here re-issues ids that live entities hold: the exact mid-exercise catastrophe
    /// the guard exists to prevent, wearing a <c>LoadingLive</c> label.</para>
    ///
    /// <para>⭐ This is why the guard asks <i>"entered FROM <c>Idle</c>?"</i> and not merely <i>"is there a
    /// <c>LoadingLive</c> step?"</i> — <c>Idle</c> is the only state with no world, so it is the only place a
    /// load CREATES one instead of branching from one.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void The_live_from_replay_branch_does_not_reset_the_authority()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingReplay);
        authority.Resets.Clear();

        // OperatingReplay -> LoadingLive -> OperatingLive: a LoadingLive step over a POPULATED world.
        RequestTransition(bus, master, ClusterState.OperatingLive);

        Assert.Empty(authority.Resets);
    }

    /// <summary>
    /// ⭐⭐ <b>A re-load DOES reset again — this is the drift §11a measured</b> *(a second
    /// <c>LoadScenarioByName</c> in one process allocating <c>1008–1015</c> because nothing reset)*.
    ///
    /// <para>⭐ Sequenced exactly as the editor sequences it: <c>EditorApplication.LoadScenarioByName</c>
    /// publishes <c>TransitionStateIntent{Idle}</c> first, waits for the cluster to reach <c>Idle</c>, and
    /// only then requests the load. ⚠ A single <c>OperatingEdit → OperatingEdit</c> intent is a NO-OP by
    /// design — the planner's BFS path to where you already are is empty — so asserting on that would have
    /// been asserting on nothing.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_second_load_resets_again()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingEdit);
        authority.Resets.Clear();

        RequestTransition(bus, master, ClusterState.Idle);            // as EditorApplication does
        RequestTransition(bus, master, ClusterState.OperatingEdit);

        Assert.Equal(new[] { WorldIdAuthority.WorldBase }, authority.Resets);
    }

    /// <summary>
    /// ⭐⭐ <b>The <c>Idle</c> a load is entered from can be in the MIDDLE of one trajectory.</b>
    /// <c>OperatingEdit → OperatingLive</c> plans
    /// <c>UnloadingEdit → Idle → LoadingLive → OperatingLive</c>. ⇒ ⛔ a guard that only compared the
    /// trajectory's SOURCE state against <c>Idle</c> would miss this and leave the live load unreset — so
    /// the walk is load-bearing, not defensive coding.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_load_entered_from_idle_mid_trajectory_still_resets()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingEdit);
        authority.Resets.Clear();

        RequestTransition(bus, master, ClusterState.OperatingLive);

        Assert.Equal(new[] { WorldIdAuthority.WorldBase }, authority.Resets);
    }

    /// <summary>
    /// 🔴 <b>Going back to <c>Idle</c> is a teardown, not a world creation.</b> ⚠ It is the case most likely
    /// to be added to the guard by mistake — it LOOKS like a reset — and it is exactly where the
    /// mid-exercise hazard lives, because a cluster returns to Idle between exercises with nodes still
    /// holding pools.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void Returning_to_idle_does_not_reset_the_authority()
    {
        var (bus, master, authority) = Cluster();
        using var _m = master;

        RequestTransition(bus, master, ClusterState.OperatingLive);
        authority.Resets.Clear();

        RequestTransition(bus, master, ClusterState.Idle);

        Assert.Empty(authority.Resets);
    }

    // ── ③ The absent authority is a legitimate answer, not a crash ────────────────────────────

    /// <summary>
    /// ⭐ A headless master with no network factory hosts no authority. ⛔ That must be silence, not a
    /// <c>NullReferenceException</c> — ⚠ and it is the one place where "nothing happened" is CORRECT, which
    /// is why every other case above had to prove that silence is not the default.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void A_master_with_no_authority_loads_without_throwing()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        RegisterNode(bus, master);

        RequestTransition(bus, master, ClusterState.OperatingLive);

        Assert.Equal(0, master.WorldIdResetCount);
    }
}
