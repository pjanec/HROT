using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Diagnostics.Breakpoints;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>MX-009</c> — THE DRAIN MUST RESUME WHEN THE BREAKPOINT THAT STOPPED IT IS GONE.</b>
/// 📄 <c>docs/blueprints/batches/HANDOFF_Observability_Gizmo_And_Smoke.md</c> §1 ·
/// <c>docs/designs/breakpoints-1/DESIGN.md</c> §9.1 *(which names only Clean Step and Continue — this
/// third release path is NEW, and the design now records it)*.
///
/// <para>🔴 <b>The defect these rails pin.</b> <c>IsRewound</c> IS <c>_isPaused</c>, and the kernel's
/// <c>ResumeAndDrainSystem</c> returns early on it. Removing a breakpoint left <c>_isPaused</c> set ⇒
/// after ANY breakpoint fired, every later staged write reported <c>pending = true</c> <b>forever</b>,
/// with no gesture that could clear it short of a Step or Continue on a breakpoint that no longer
/// existed. 📌 <c>M-41</c>'s "accepted and silently discarded", in its worst form.</para>
///
/// <para>⚠⚠ <b>These are MANAGER-level rails, and that is the honest scope.</b> ⛔ They do not run the
/// kernel — <c>ResumeAndDrainSystem</c> is the time lane's. ⭐ What they assert is the exact predicate
/// that system branches on (<c>IsRewound</c>) plus the drain actually moving the bytes, which is the
/// whole of this class's contribution to the reported repro.</para>
/// </summary>
[Collection("ComponentRegistry")]
public sealed class TheDrainResumesAfterTheHolderIsGoneTests
{
    private static (DataBreakpointManager manager, EntityRepository repo, Entity entity, BreakpointId id) Fired()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 1 });

        var id = manager.Add(ManagerFactory.MakeBreakpoint(name: "holder"));
        manager.OnHit(manager.AllBreakpoints[0], entity);

        // ⛔ Anti-vacuity: the rails below are meaningless unless the pause actually engaged.
        Assert.True(manager.IsPaused);
        Assert.True(((IStagedWrites)manager).IsRewound);

        return (manager, liveRepo, entity, id);
    }

    /// <summary>⭐⭐⭐ The reported repro: fire, delete, and the drain gate must be OPEN again.</summary>
    [Fact]
    public void RemovingTheHolder_ReleasesTheRewind()
    {
        var (manager, _, _, id) = Fired();

        manager.Remove(id);

        Assert.False(manager.IsPaused);
        Assert.False(((IStagedWrites)manager).IsRewound);
    }

    /// <summary>
    /// ⭐⭐ <b>And the staged bytes actually LAND.</b> ⚠ Railed separately from the flag because
    /// "the gate opened" and "the write applied" are two claims, and the first one passing while the
    /// second fails is precisely the shape of the bug being fixed.
    /// </summary>
    [Fact]
    public void AfterTheHolderIsRemoved_AStagedWriteDrains()
    {
        var (manager, repo, entity, id) = Fired();

        manager.Remove(id);
        manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 42 });

        var staged = (IStagedWrites)manager;
        Assert.True(staged.HasPending);
        Assert.False(staged.IsRewound);          // ⭐ what ResumeAndDrainSystem branches on

        // ⭐ Same shape as PendingMutationTests.Drain_UnmanagedPayload_PinnedAndCopiedToECB: the drain
        //   writes into the repo's command buffer, and the buffer is played back at the tick boundary
        //   (in production, by the kernel).
        var ecb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        staged.DrainInto((ISimulationView)repo);
        ecb.Playback(repo);

        Assert.Equal(42, repo.GetComponent<TestHealth>(entity).Current);
        Assert.False(staged.HasPending);
    }

    /// <summary>⭐ Disabling the holder is the same act — it can no longer fire, so it cannot hold.</summary>
    [Fact]
    public void DisablingTheHolder_ReleasesTheRewind()
    {
        var (manager, _, _, id) = Fired();

        manager.SetEnabled(id, false);

        Assert.False(manager.IsPaused);
        Assert.False(((IStagedWrites)manager).IsRewound);
    }

    /// <summary>
    /// ⛔⛔ <b>THE RULE'S OTHER HALF, and the reason it is "held-by" rather than "none remain".</b>
    /// ⭐ Removing a DIFFERENT breakpoint must NOT resume — the operator is still stopped at the one
    /// they are reading. ⚠ Without this rail the cheapest fix ("resume on any removal") passes
    /// everything above while ripping the world out from under an inspection.
    /// </summary>
    [Fact]
    public void RemovingSomeOtherBreakpoint_DoesNotRelease()
    {
        var (manager, _, _, _) = Fired();
        var bystander = manager.Add(ManagerFactory.MakeBreakpoint(name: "bystander"));

        manager.Remove(bystander);

        Assert.True(manager.IsPaused);
        Assert.True(((IStagedWrites)manager).IsRewound);
    }

    /// <summary>
    /// ⚠ <b>A resumed manager must not carry a stale holder.</b> ⭐ After a Continue, removing the
    /// breakpoint that USED to hold the pause must be an ordinary removal — ⛔ if <c>_pausedBy</c>
    /// survived the resume, a later re-pause by a different breakpoint could be released by removing
    /// the wrong one. 📌 This is why the clear lives in one method both release paths call.
    /// </summary>
    [Fact]
    public void AfterAContinue_TheHolderIsForgotten()
    {
        var (manager, _, entity, id) = Fired();
        manager.RequestContinue();
        Assert.False(manager.IsPaused);

        // A different breakpoint now fires and holds the pause.
        var second = manager.Add(ManagerFactory.MakeBreakpoint(name: "second"));
        manager.OnHit(System.Linq.Enumerable.Single(manager.AllBreakpoints, b => b.Id.Equals(second)), entity);
        Assert.True(manager.IsPaused);

        // Removing the FIRST (stale) one must not release the SECOND one's pause.
        manager.Remove(id);
        Assert.True(manager.IsPaused);

        manager.Remove(second);
        Assert.False(manager.IsPaused);
    }
}
