using System.Collections.Generic;
using Fdp.Interfaces;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE base TKB→ECS projection list. Every host that spawns from TKB uses this.</b>
    /// 📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §5 step 2 · <c>docs/designs/tkb-1/DESIGN.md</c>
    /// §6.3 · §6.5 · §6.5b.
    ///
    /// <para>📌 <b>Why this type exists.</b> The list was written inline at <b>six</b> composition roots,
    /// and <b>five</b> of them were measured wrong in one week — always the same way: an optional
    /// <c>translators</c> parameter with a silent <c>Array.Empty</c> default, at a site whose author held
    /// the value. SimHost (<c>S1</c>), the Editor and the Stride editor (<c>CE-137</c>), CGF
    /// (<c>CE-138</c>) and the Stride node (<c>CE-139</c>). ⇒ 🔒 <b>the convention in §6.3 —
    /// <i>"identical for all three systems within the same node"</i> — was true and unenforced.</b></para>
    ///
    /// <para>⛔⛔ <b>Do NOT subtract from this list to make a host materialise less.</b> That is not the
    /// narrowing lever, and a short list fails silently for every entity the host ever spawns. Every
    /// <see cref="ITkbEntityTranslator"/> is contractually required to guard each write with
    /// <c>repo.IsComponentTypeRegistered&lt;T&gt;()</c>, so a translator whose components a host never
    /// registered is <b>already</b> a no-op there. ⇒ ⭐ <b>express "this host does not want X" by not
    /// registering X</b> in the host's component registry — one loud decision, and any code that then
    /// tries to write it throws. 📄 <c>tkb-1/DESIGN.md</c> §6.5b.</para>
    ///
    /// <para>⭐ <b>Additions are fine and are how per-node variation is meant to work</b> — §6.5:
    /// <i>"an IG node would include BIG-specific translators; a SimHost node would not."</i> Concatenate
    /// onto <see cref="Base"/>; do not fork it. Two live examples that cannot live here, both for
    /// reference-graph reasons rather than policy: <c>AiDiagnosticsTkbTranslator</c>
    /// (<c>Hrot.SimHost</c>) and <c>InfantryVehicleStateStripTkbTranslator</c>
    /// (<c>Hrot.Stride.Core</c>) — both sit ABOVE <c>Hrot.Core</c>.</para>
    ///
    /// <para>⚠ <b>A host with no TKB spawn path needs none of this.</b> IG deliberately forwards
    /// <c>SpawnEntityCommand</c> to SimHost and receives the ghost back, so its translators go only to
    /// <c>NedReplicationModule</c>'s ghost projection. ⛔ That is a coherent configuration, not an
    /// omission — do not "fix" it by giving IG a spawn pipeline.</para>
    ///
    /// <para>🔴🔴 <b><see cref="BasePlus"/> APPENDS, so it CANNOT express a POSITIONAL contract — and one
    /// translator has one.</b> <c>InfantryVehicleStateStripTkbTranslator</c>'s own doc requires it
    /// <i>"immediately after <c>VehicleKinematicsTkbTranslator</c> … position in the list is the
    /// guarantee"</i>, because <c>NetworkSpawningSystem.ProcessSpawn</c> runs
    /// <c>foreach (var t in _translators) t.Inject(…)</c> in order. 📐 <b>Measured 2026-08-31:</b>
    /// <c>CE-140</c> step 2 converted <c>EditorStrideSubsystem.BuildTranslators()</c> from a hand-written
    /// list — where the strip sat at index 2, right after kinematics — to <c>BasePlus(strip)</c>, which
    /// puts it LAST (index 6). ⇒ <b>the documented contract is violated today.</b></para>
    ///
    /// <para>⚠ <b>Stated honestly: that is a latent contract violation, NOT a known behaviour bug.</b>
    /// The strip is a pure removal of <c>VehicleState</c>/<c>VehicleParams</c>, and only
    /// <c>VehicleKinematicsTkbTranslator</c> ever adds them, so running the strip later still yields the
    /// same END STATE. ⛔ It becomes a real defect the moment any translator between the two READS
    /// <c>VehicleState</c>. ⇒ ⭐ <b>Before adding an order-sensitive translator, give this class an
    /// explicit insert-after helper rather than reaching for <see cref="BasePlus"/>.</b>
    /// 📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.3.</para>
    /// </summary>
    public static class TkbTranslatorSet
    {
        /// <summary>
        /// The base projection set, in dependency order: spatial core first (it zero-initialises the
        /// spatial chunks the others build on), then kinematics, behaviour, combat, perception, and the
        /// map presentation family last.
        /// </summary>
        /// <remarks>
        /// ⭐ Returns a fresh read-only list per call — translators are stateless, but a caller must be
        /// free to concatenate its own without mutating anyone else's view.
        /// ⚠ Pass the SAME returned instance to <c>NetworkSpawningSystem</c>,
        /// <c>EntityLifecycleModule.SetTranslators</c> and <c>GhostPromotionSystem</c> within one node —
        /// §6.3. ⛔ Calling <c>Base()</c> three times in one composition root defeats the point.
        /// </remarks>
        public static IReadOnlyList<ITkbEntityTranslator> Base() => new List<ITkbEntityTranslator>
        {
            new Fdp.Toolkit.Spatial.SpatialCoreTkbTranslator(),
            new CarKinem.Tkb.VehicleKinematicsTkbTranslator(),
            new Fdp.Toolkit.Behavior.Translators.BehaviorTkbTranslator(),
            new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator(),
            new Fdp.Toolkit.Perception.Translators.PerceptionTkbTranslator(),
            // Writes VisualData (SymbolCode = the MIL-STD-2525 SIDC, ColorHex, MapShapeName) and
            // derives EntityInfo.ForceId from the SIDC's affiliation character.
            new Hrot.Map.Definitions.Tkb.PresentationTkbTranslator(),
        }.AsReadOnly();

        /// <summary>
        /// <see cref="Base"/> plus <paramref name="extra"/>, for nodes with translators that live above
        /// <c>Hrot.Core</c>. ⭐ Add-only by design: there is no overload that removes.
        /// </summary>
        public static IReadOnlyList<ITkbEntityTranslator> BasePlus(
            params ITkbEntityTranslator[] extra)
        {
            var list = new List<ITkbEntityTranslator>(Base());
            if (extra != null) list.AddRange(extra);
            return list.AsReadOnly();
        }
    }
}
