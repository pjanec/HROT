using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Events;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-051</c> (Axis-C <b>E3</b>) — center the map camera on an entity, SHARED.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c></b> §3 ②, §5, and **§6 — this system is the
/// two-way reconciliation the design warned about, and it found a live bug.**
///
/// <para>🔴🔴 <b>MEASURED <c>2026-08-26</c>: CGF's hand-rolled `CenterCameraOnEntity` DID NOT WORK.</b>
/// It set <c>MapCamera.Target</c> *(i.e. <c>InnerCamera.Target</c>)* directly and never touched the
/// camera's <c>_targetTarget</c>. ⛔ But <c>MapCamera.Update</c> assigns
/// <c>InnerCamera.Target = _targetTarget</c> every frame *(`EnableSmoothing` defaults to
/// <see langword="false"/>, so it is an outright overwrite, not a lerp)*. ⇒ ⭐⭐ **the centre was undone on
/// the very next frame, snapping the view back to `_targetTarget` — which CGF never sets, so it is
/// `Vector2.Zero`.** ⚠ *"Center on entity"* on CGF sent the camera to the origin.
/// ⭐ The editor's arm called <c>FocusOn</c>, which sets <c>_targetTarget</c> — the correct seam.</para>
///
/// <para>⭐⭐ <b>And CGF's arm was BETTER in one respect, so the survivor is a MERGE</b> *(the same shape as
/// E2's create-core)*: it preferred <c>NetworkTransform.LastPosition</c> and fell back to
/// <c>SimTransform</c>, while the editor read <c>SimTransform</c> only. ⚠ On a host that does not OWN
/// <c>SimTransform</c> the replicated position is the fresher one — 📌 exactly the `AX-005b` insight that
/// gave the rotate gizmo an <c>EntityWriteRouter</c>. ⇒ ⭐ this system takes <b>CGF's component
/// preference</b> and <b>the editor's camera seam</b>.</para>
/// </summary>
/// <remarks>
/// ⚠⚠ <b><c>[UpdateInPhase(PostSimulation)]</c> — and its ABSENCE was a BOOT CRASH, caught by T3.</b>
/// 📐 <c>SystemScheduler.RegisterSystem</c> throws <c>"System X must have [UpdateInPhase] attribute"</c>, so
/// <c>kernel.Initialize()</c> — and therefore the whole editor — failed to start. ⛔ Every unit rail passed:
/// the test's recording registry accepted any system, so it never asked the question the real scheduler asks.
/// ⇒ ⭐ the rail now asserts the attribute is present *(see <c>TheViewportInteractionIsSharedTests</c>)*.
///
/// <para>⭐ <b>Why <c>PostSimulation</c>:</b> 📐 the editor's old drain ran from <c>EditorSubsystem.Update()</c>
/// at <c>:2239</c>, AFTER <c>_kernel.Update()</c> at <c>:2232</c> — so a gizmo it activated first executed on
/// the following frame either way. ⭐ <c>PostSimulation</c> is the main-thread phase the gizmo group and the
/// sibling <c>CanvasMenuUpdateSystem</c> already use, which keeps this within one frame of the old ordering.
/// ⛔ Not <c>Simulation</c>: that runs on background threads and this touches ImGui-adjacent host state.</para>
/// </remarks>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class CenterOnEntitySystem : IEcsModuleSystem
{
    private readonly Func<MapCamera?> _camera;

    /// <param name="camera">
    /// ⭐ A delegate, not the camera: both hosts create their <c>MapCanvas</c> during window
    /// registration, which can run after the module is built — and the editor's canvas is replaced on a
    /// perspective switch. ⛔ Capturing the instance would silently centre a dead camera.
    /// </param>
    public CenterOnEntitySystem(Func<MapCamera?> camera)
        => _camera = camera ?? throw new ArgumentNullException(nameof(camera));

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        foreach (ref readonly var cmd in world.Bus.Read<CenterOnEntityCommand>())
        {
            var camera = _camera();
            if (camera == null) continue;

            // ⭐ BP-508 — the ONE resolver (R-77).
            var target = NetworkIdResolver.FindEntityByNetworkId(world, cmd.NetworkId);
            if (target.IsNull) continue;
            if (!TryResolvePosition(world, target, out var pos)) continue;

            // ⭐⭐⭐ FocusOn, NOT `Camera.Target = pos` — see the class remarks: the direct assignment is
            //    overwritten by MapCamera.Update on the next frame, which is the CGF bug this replaces.
            camera.FocusOn(pos);
        }
    }

    /// <summary>
    /// ⭐⭐ <b><c>NetworkTransform</c> first, <c>SimTransform</c> second — CGF's ordering, kept.</b>
    /// ⚠ On a non-owning host the replicated transform is the fresher of the two; on the editor *(which
    /// owns everything it shows)* the two agree, so taking the better ordering costs the editor nothing.
    /// </summary>
    private static bool TryResolvePosition(EntityRepository world, Entity e, out Vector2 pos)
    {
        if (world.HasComponent<NetworkTransform>(e))
        {
            ref readonly var nt = ref world.GetComponentRO<NetworkTransform>(e);
            pos = new Vector2(nt.LastPosition.X, nt.LastPosition.Y);
            return true;
        }

        if (world.HasComponent<SimTransform>(e))
        {
            ref readonly var st = ref world.GetComponentRO<SimTransform>(e);
            pos = new Vector2(st.Position.X, st.Position.Y);
            return true;
        }

        pos = default;
        return false;
    }
}
