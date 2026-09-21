using Fdp.ModuleHost.Abstractions;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐ Schedules <see cref="SelectionInteractionSystem"/> on a kernel.
///
/// <para>📐 <b>Why an adapter rather than making the system an <see cref="IEcsModuleSystem"/>:</b>
/// three hosts tick it from their own map update (editor, SimHost, ReplayBrowser) and two schedule it
/// (IG, CGF). ⛔ If it implemented the interface, a host that does both — or that gains a kernel later
/// — would tick it TWICE per frame, and a double tick here means the Delete key destroying on one
/// pass and clearing on the next. ⭐ An explicit adapter makes "scheduled" a choice a host states.</para>
///
/// <para>⚠ <b>It was IG-private until <c>UXI-11</c> <c>S-4</c>.</b> 📐 CGF needed exactly this and could
/// not see it, which is part of why CGF had no map-input path at all — the other part being that
/// nothing had noticed the host was never given one.</para>
/// </summary>
/// <remarks>
/// ⚠ <c>[UpdateInPhase(PostSimulation)]</c> is carried over from the IG-private original and is not
/// decoration: the main-thread phase the gizmo group already uses. ⛔ Not <c>Simulation</c> — that runs
/// on background threads and this touches host-adjacent state.
/// </remarks>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class SelectionInteractionSystemAdapter : IEcsModuleSystem
{
    private readonly SelectionInteractionSystem _system;

    public SelectionInteractionSystemAdapter(SelectionInteractionSystem system)
        => _system = system ?? throw new System.ArgumentNullException(nameof(system));

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime) => _system.Tick(deltaTime);
}
