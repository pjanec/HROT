using System;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.ScenarioEditor.Services;
using Hrot.ScenarioEditor.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.ScenarioEditor;

/// <summary>
/// Entry-point <see cref="IEcsModule"/> for the Scenario Editor shared interaction logic.
///
/// <para>⭐⭐⭐ <b><c>CE-051</c> (Axis-C <b>E3</b>) — <c>RegisterSystems</c> IS POPULATED. This finishes
/// <c>PACK2-E002</c>, whose stub comment reserved this exact spot and was never filled in.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c></b> §1, §3 ②, §4.</para>
///
/// <para>⭐⭐ <b>Both hosts register this module over their own viewport primitives</b> — the editor over
/// its canvas/selection/gizmo stack, CGF over its. ⛔ That is what collapses the drift §6 warned about:
/// before E3 the editor drove tools from a drain welded into a 5 000-line host and CGF drove them from
/// hand-rolled context-menu callbacks that had independently diverged.</para>
///
/// <para>⚠ <b>The interaction systems are OPT-IN.</b> A host that supplies no
/// <see cref="ISelectionState"/> / gizmo system — a headless node, or a unit test constructing the module
/// only for its <see cref="FileService"/> — registers <b>no</b> systems and behaves exactly as before.
/// ⛔ Not a silent default: the deps are constructor parameters, so a windowed host that HAS them and
/// omits them is a wiring bug, ⭐ and a host that genuinely lacks them is stating a fact
/// *(CLAUDE.md's silent-default rule — the caller that HAS a dependency must pass it)*.</para>
/// </summary>
public class ScenarioEditorModule : IEcsModule
{
    private readonly ScenarioFileService? _fileService;
    private readonly InteractionDeps?     _interaction;

    /// <summary>
    /// ⭐ The viewport dependencies the shared interaction systems need. Grouped into one record so the
    /// module's ctor does not grow six optional parameters whose combinations are meaningless — ⛔ either
    /// a host has a viewport or it does not.
    /// </summary>
    /// <para>⭐⭐⭐ <b>EVERY member is a RESOLVER, not an instance — and this is the load-bearing as-built
    /// correction of E3.</b> 📐 Measured <c>2026-08-26</c> *(the HN-037 "check the captures" rule, which is
    /// exactly what caught it)*: in <c>EditorSubsystem</c> the module is constructed at <c>:1273</c> and
    /// <c>kernel.Initialize()</c> — which calls <see cref="RegisterSystems"/> — runs at <c>:1733</c>, but
    /// <c>_camera</c> is created at <c>:1801</c>, <c>_spawnAdapter</c> at <c>:1942</c> and
    /// <c>_selectionState</c> at <c>:1945</c>. ⛔ <b>All THREE are null when the systems are built</b>, and
    /// worse, all three are set back to <c>null</c> on teardown *(<c>:4756-4775</c>)*. ⇒ ⛔ capturing
    /// instances would have wired the systems to permanent nulls, silently — no exception, no log, just a
    /// dead tool set. ⭐ Resolvers make the systems correct across the host's whole build/teardown cycle.</para>
    /// <param name="Selection">Resolves the host's persistent viewport selection at USE time.</param>
    /// <param name="Gizmos">Resolves the host's entity-scoped gizmo injector at USE time.</param>
    /// <param name="Camera">Resolves the host's map camera at USE time.</param>
    /// <param name="GlobalGizmos">Needed by the Measure tool only; <c>null</c> ⇒ Measure reports unserviceable.</param>
    /// <param name="StartPlacementMode">The Spawn tool's behaviour; <c>null</c> ⇒ Spawn reports unserviceable.</param>
    /// <param name="AlsoSelect">Optional host follow-through after a selection write (e.g. an inspector panel).</param>
    public sealed record InteractionDeps(
        Func<ISelectionState?>        Selection,
        Func<DataDrivenGizmoSystem?>  Gizmos,
        Func<MapCamera?>              Camera,
        Func<GlobalGizmoManager?>?    GlobalGizmos       = null,
        Action?                       StartPlacementMode = null,
        Action<Entity>?               AlsoSelect         = null);

    public ScenarioEditorModule(
        ScenarioFileService? fileService = null,
        InteractionDeps?     interaction = null)
    {
        _fileService = fileService;
        _interaction = interaction;
    }

    public string Name => "ScenarioEditor";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    /// <summary>
    /// Exposes the file service for use by panels that trigger New/Save/Load operations.
    /// <c>null</c> when no serializer was provided at construction time.
    /// </summary>
    public ScenarioFileService? FileService => _fileService;

    /// <summary>True when this host wired the viewport interaction systems.</summary>
    public bool HasInteractionSystems => _interaction != null;

    public void RegisterSystems(ISystemRegistry registry)
    {
        // ⭐⭐⭐ PACK2-E002, finished by CE-051. The render-layer half (PACK2-E003) is still open.
        if (_interaction is not { } deps) return;

        registry.RegisterSystem(new ToolActivationDrainSystem(
            deps.Selection, deps.Gizmos, deps.GlobalGizmos, deps.StartPlacementMode));
        registry.RegisterSystem(new SelectEntitySystem(deps.Selection, deps.AlsoSelect));
        registry.RegisterSystem(new CenterOnEntitySystem(deps.Camera));
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
