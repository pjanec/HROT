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
    /// <para>⛔⛔ <b>SUPERSEDED <c>2026-09-03</c> (<c>CE-144</c> + <c>CE-141</c>). An earlier version of
    /// this paragraph read:</b> <i>"A host with no TKB spawn path needs none of this. IG deliberately
    /// forwards SpawnEntityCommand to SimHost and receives the ghost back … That is a coherent
    /// configuration, not an omission — do not 'fix' it by giving IG a spawn pipeline."</i>
    /// 🔴 <b>Every clause of that is now false</b>, and it sat in SHARED code telling every host the
    /// opposite of what is built: IG schedules the shared <c>NetworkSpawningSystem</c>, its tools post
    /// creation INTENTS rather than forwarding node-local orders, and it no longer calls
    /// <c>.WithTranslators(...)</c> at all.</para>
    ///
    /// <para>⭐⭐⭐ <b>THE RULE, and it now has no exceptions:</b> 🔒 <i>"entity creation needs to be
    /// unified. There should be nothing we give just to IG. every ECS nodes must use same TKB in same way
    /// using the same shared code."</i> (user, <c>2026-09-03</c>). ⇒ <b>no ECS node builds a TKB
    /// translator list.</b> <c>EntityCreationPack.Build</c> composes it — <see cref="Base"/>, or
    /// <see cref="BasePlus"/>/<see cref="BaseWith"/> when a host contributes ADDITIONS — hands that ONE
    /// instance to the ELM and to <c>NetworkSpawningSystem</c>, and <c>GhostPromotionSystem</c> reads it
    /// back off the ELM. ⛔ <b>A composition root that names translators is a defect now, not a
    /// configuration.</b></para>
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
    ///
    /// <para>✅ <b><c>CE-146</c>, <c>2026-09-02</c> — that helper now exists: <see cref="BaseWith"/> +
    /// <see cref="TranslatorPlacement"/>.</b> It is what lets the Stride editor join
    /// <c>EntityCreationPack</c> without dropping the positional contract. 🔒 <c>R-137</c>: <i>"we should
    /// not lose flexibility of the features, if unification takes some away, this is a signal we should
    /// think how to put it back (via configuration for example)."</i> ⇒ the placement IS that
    /// configuration. ⛔ It states the anchor as a TYPE, never an index — an index silently re-aims when
    /// <see cref="Base"/> gains an entry, and a missing anchor THROWS rather than appending.</para>
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

        /// <summary>
        /// ⭐⭐ <see cref="Base"/> plus <paramref name="placements"/>, each either APPENDED or inserted
        /// IMMEDIATELY AFTER a named translator type. ⭐ This is the order-sensitive form
        /// <see cref="BasePlus"/> cannot express (<c>CE-146</c>); <see cref="BasePlus"/> is now a thin
        /// all-append call onto it.
        ///
        /// <para>⛔ <b>A placement whose anchor type is absent THROWS.</b> Appending instead would be the
        /// SILENT-DEFAULT shape this whole family exists to kill: the caller stated an ordering contract
        /// and would get a list that quietly does not honour it.</para>
        ///
        /// <para>⭐ Placements are applied in the order given, so an anchor may itself be a translator
        /// added by an earlier placement.</para>
        /// </summary>
        public static IReadOnlyList<ITkbEntityTranslator> BaseWith(
            params TranslatorPlacement[] placements)
        {
            var list = new List<ITkbEntityTranslator>(Base());
            if (placements == null) return list.AsReadOnly();

            foreach (var p in placements)
            {
                if (p.Translator == null) continue;

                var afterType = p.AfterType;
                if (afterType == null)
                {
                    list.Add(p.Translator);
                    continue;
                }

                int anchor = list.FindIndex(t => afterType.IsInstanceOfType(t));
                if (anchor < 0)
                {
                    throw new System.InvalidOperationException(
                        $"TkbTranslatorSet.BaseWith: cannot place {p.Translator.GetType().Name} after " +
                        $"{afterType.Name} — no translator of that type is in the list. The ordering " +
                        "contract cannot be honoured, and appending silently would hide that.");
                }
                list.Insert(anchor + 1, p.Translator);
            }
            return list.AsReadOnly();
        }
    }

    /// <summary>
    /// ⭐ One translator plus WHERE it goes relative to <see cref="TkbTranslatorSet.Base"/>.
    /// 📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.3 · <c>CE-146</c>.
    /// </summary>
    public readonly struct TranslatorPlacement
    {
        /// <summary>The translator to add.</summary>
        public ITkbEntityTranslator Translator { get; }

        /// <summary>
        /// The translator TYPE this one must immediately follow, or <c>null</c> to append.
        /// ⛔ A type, never an index — an index re-aims silently when <see cref="TkbTranslatorSet.Base"/>
        /// gains an entry.
        /// </summary>
        public System.Type? AfterType { get; }

        private TranslatorPlacement(ITkbEntityTranslator translator, System.Type? after)
        {
            Translator = translator;
            AfterType = after;
        }

        /// <summary>Add <paramref name="translator"/> at the END of the list.</summary>
        public static TranslatorPlacement Append(ITkbEntityTranslator translator)
            => new TranslatorPlacement(translator, null);

        /// <summary>
        /// Add <paramref name="translator"/> IMMEDIATELY AFTER the first translator assignable to
        /// <typeparamref name="TAfter"/>. ⛔ Throws at <see cref="TkbTranslatorSet.BaseWith"/> time if
        /// no such translator is present.
        /// </summary>
        public static TranslatorPlacement After<TAfter>(ITkbEntityTranslator translator)
            where TAfter : ITkbEntityTranslator
            => new TranslatorPlacement(translator, typeof(TAfter));
    }
}
