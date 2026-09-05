using Fdp.Core;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common;

/// <summary>
/// Shared presentation component registrations used by multiple nodes.
///
/// <para>⭐⭐⭐ <b><c>CE-065</c> — THE ONE LIST. Every event the SHARED viewport systems read is registered
/// HERE, on the WORLD's bus, so a host that adopts the shared systems cannot forget one.</b>
/// 📄 <c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c> §3 *(the <c>E3</c> systems)* ·
/// <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5.7.</para>
///
/// <para>🔴🔴 <b>The live crash this closed, measured `2026-08-27`.</b>
/// <c>Hrot.ClusterRunner/Program.cs:52</c> sets <c>FdpConfig.EnforceExplicitEventRegistration = true</c>
/// <b>process-wide</b>, under which <c>Publish&lt;T&gt;</c> THROWS for an unregistered type. ⛔
/// <c>CenterOnEntityCommand</c> and <c>ActivateEditorToolEvent</c> were registered ONLY inline in
/// <c>EditorSubsystem</c> *(<c>:917-918</c>)*, so on CGF *"Center on entity"* threw out of the ImGui context
/// menu — the user's reported crash. 📐 Reproduced over MCP:
/// <c>POST /entities/1000/focus</c> → <c>500 Strict Mode Violation … (ID: 8104)</c>.</para>
///
/// <para>⭐⭐ <b>The seam already existed and was under-adopted</b> — the 25th measured instance of that
/// pattern here. <see cref="Hrot.Common.Events.SelectEntityCommand"/> was ALREADY on this list, which is
/// exactly why *"Select entity"* worked on CGF while *"Center on entity"* crashed: ⚠ <b>two sibling menu
/// items from the same slice, one registered centrally and one inline.</b> ⇒ the fix is not a new registry,
/// it is putting the other two where the first one already lived *(ruling 9: one implementation)*.</para>
///
/// <para>⭐ <b>Adopters</b> *(measured)*: <c>CgfComponentRegistry</c> · <c>SimHostComponentRegistry</c> ·
/// <c>StrideNodeBootstrapper</c> — and the EDITOR transitively, since <c>EditorSubsystem:905</c> calls
/// <c>CgfComponentRegistry.RegisterAll(_world)</c>. ⇒ one edit here reaches every windowed host.</para>
///
/// <para>⚠ <b>Not to be confused with <c>OrchestrationEventRegistry</c></b>, which does the same job for the
/// TIME/ORCHESTRATION intents on the NODE's bus *(<c>HrotNodeBuilder:112</c>)*. 📌 That registry exists
/// because of the identical bug one bus over — *"pressing pause on a CGF/SimHost/IG toolbar throws instead
/// of pausing"* — so this is the second time the same shape has been paid for. ⭐ Two buses, two lists, and
/// each list is the ONLY one for its bus.</para>
/// </summary>
public static class PresentationComponentRegistry
{
    /// <summary>
    /// Registers presentation-oriented ECS components into <paramref name="world"/>.
    /// </summary>
    /// <remarks>
    /// ⭐ Idempotent: every call resolves to <c>GetOrCreate…</c>, so a host that reaches this twice
    /// *(the editor registers into both <c>_world</c> and its pre-tick snapshot)* is fine.
    /// </remarks>
    public static void RegisterAll(EntityRepository world)
    {
        world.RegisterComponent<EntityInfo>();
        world.RegisterComponent<SelectionState>();
        world.RegisterManagedComponent<EditablePolyline>();
        world.RegisterComponent<MapOverlayStyle>();
        // ⭐⭐⭐ CE-196 — this used to register `IgHealthState`, a render-only cache holding a precomputed
        //    damage percentage. It is GONE; `Health` takes its place so every presentation host can read
        //    the SAME component the authority owns, and `HealthBarGizmo`/`StyleResolutionSystem` derive
        //    the fraction themselves. 🔒 User ruling, 2026-09-05: "no precalculated percentages".
        // ⚠ Registering it HERE matters: this registry is reached by Stride, CGF, SimHost AND the editor
        //   (four call sites), so the cache existed on every presentation host — not only IG. Dropping it
        //   without putting `Health` in its place would leave `HealthBarGizmo` projecting a component
        //   those hosts never registered. ⭐ The call is idempotent (GetOrCreate), so hosts that already
        //   register `Health` through their combat registry are unaffected.
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterManagedEvent<Hrot.Common.TogglePerspectiveEvent>();
        world.RegisterEvent<Hrot.Common.Events.WorldResetEvent>();
        world.RegisterEvent<Hrot.Common.Events.OpenRenameDialogCommand>();
        // ══ the SHARED VIEWPORT systems' events (ScenarioEditorModule.RegisterSystems) ══════════
        // ⭐⭐⭐ CE-065 — the three shared systems read exactly these three events, and all three are now
        //    on ONE list. 📐 Enumerated `2026-08-27` from the systems themselves, not guessed:
        //      · SelectEntitySystem        -> SelectEntityCommand     (the one that was already here)
        //      · CenterOnEntitySystem     -> CenterOnEntityCommand
        //      · ToolActivationDrainSystem -> ActivateEditorToolEvent
        // ⛔ The last two used to be registered ONLY inline in EditorSubsystem:917-918, so every OTHER
        //    adopter of the shared systems published them into a bus that had never heard of them — and
        //    under the runner's process-wide strict mode that THROWS. 🔴 On CGF that was the user's
        //    `2026-08-27` crash: "Center on entity" in the entity-inspector context menu.
        // ⚠ If a fourth system joins ScenarioEditorModule, its event belongs HERE, in the same commit.
        world.RegisterEvent<Hrot.Common.Events.SelectEntityCommand>();
        world.RegisterEvent<Hrot.Common.Events.CenterOnEntityCommand>();
        world.RegisterEvent<Hrot.Common.Events.ActivateEditorToolEvent>();
    }
}
