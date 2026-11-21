#nullable enable
using Fdp.Core;

namespace Hrot.Editor;

/// <summary>
/// The role the editor declares. Its capabilities come from the ONE role table, <see cref="Hrot.SimHost.RoleCapabilityTable"/>.
/// </summary>
/// <remarks>
/// ⭐⭐⭐ <c>CE-4270</c> B9 (<c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1ag, R-3077). Until B9 this class held the editor's own copy of the role
/// table (<c>BuildDefault</c> / <c>BuildWithInjectedMuscle</c>) and its own Brain, MuscleGround and EQS-solver wrapper classes. The wrappers differed from CGF's
/// and SimHost's only in leaving the two logic packs unregistered as modules. Both packs are empty shells as modules (<c>RegisterSystems</c> and <c>Tick</c>
/// do nothing; their systems run from the togglable groups), and the kernel disposes no module. So the fold changed the editor's module list (two rows) and
/// nothing that runs. ⛔ The copy is where the editor's drift lived: it once lacked the navigation solver (CE-3017) and the EQS solver (EQS design §17.3).
/// </remarks>
public static class EditorCapabilities
{
    /// <summary>
    /// The role the editor declares.
    ///
    /// <para>🔒 <b>User ruling, 2026-09-07:</b> <i>"editor's subsystem role could be 'everything' what
    /// Cgf+SimHost (probably not so much the IG) do now, for the purpose of editing scenarios and AI
    /// behaviors and testing them quickly."</i></para>
    ///
    /// <para>So: CGF ∪ SimHost, and deliberately <b>not</b> <c>ImageGenerator</c>. The purpose clause is
    /// the load-bearing half — the editor exists to author scenarios and behaviours and test them fast
    /// in one process, which is precisely "be both nodes at once". A role this wide weakens
    /// role-selection as a <i>narrowing</i> device, and that is fine here: what the editor takes from
    /// the seam is one shared declaration and shared units, not narrowing.</para>
    ///
    /// <para>⚠ <c>NavigationSolver</c> is declared because the editor's muscle pack contains the
    /// navigation systems, and <c>§4.1v</c> is the reason to say so rather than leave it off: SimHost
    /// declared <c>MuscleGround|Perception</c> while composing all three, and narrowing to that
    /// declaration would have silently dropped two modules. The declaration is made true, not
    /// convenient.</para>
    /// </summary>
    /// <para>⭐ AQ93 R-4047 — <c>Traffic</c>: the editor embeds SUMO (the networkless host first). The pack is idle unless the terrain
    /// has a SUMO net and the scenario a traffic entity, so declaring it costs nothing when there is no traffic.</para>
    public const NodeRole DefaultRole =
        NodeRole.Brain | NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver | NodeRole.Traffic;
}
