using Fdp.Core;
using Fdp.Interfaces;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐⭐ <b>The ONE place that applies HROT's component conventions to a <see cref="TkbTemplate"/>
    /// that was built without them</b> — i.e. every template loaded from a TKB <b>file</b>.
    ///
    /// <para>📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6 (the principle) ·
    /// <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1, §6b · <c>CE-259az</c>.</para>
    ///
    /// <para>⛔⛔ <b>THE GAP THIS CLOSES.</b> <c>TkbDeserializer.ParseAndRegister</c> builds a template
    /// <b>purely from descriptor keys</b> — every JSON property is dispatched to a
    /// <c>TkbDescriptorRegistry</c> parser thunk, and unknown keys are silently skipped. ⇒ a file-loaded
    /// template has an <b>EMPTY</b> <see cref="TkbTemplate.BirthCriticalComponents"/> and an <b>EMPTY</b>
    /// <see cref="TkbTemplate.MandatoryComponents"/>, because neither list is descriptor-shaped and the
    /// file has no way to say either one. ⚠ The programmatic catalogues fill them by hand; the file path
    /// never could.</para>
    ///
    /// <para>⚠ <b>Why a convention and NOT a new TKB field.</b> A file field would have to name components
    /// as STRINGS, and <c>ComponentTypeRegistry</c> is keyed by <c>Type</c> with no name→Type map — so it
    /// would need a reflection scan plus loud load-time validation, because
    /// <c>TkbDeserializer</c> <b>silently skips</b> what it does not recognise and a typo'd component name
    /// would vanish without a word. ⛔ That is a real schema change with a real failure mode, for a value
    /// that does not currently vary. ⭐ When it DOES vary, the field is the right answer — see the
    /// mandatory half below, which is exactly that case.</para>
    /// </summary>
    public static class TkbComponentConventions
    {
        /// <summary>
        /// Applies every convention a file-loaded template cannot express. Idempotent — the
        /// <c>Add…</c> helpers on <see cref="TkbTemplate"/> already guard against duplicates.
        /// </summary>
        public static void ApplyTo(TkbTemplate template)
        {
            if (template == null) return;

            // ⭐⭐⭐ BIRTH-CRITICAL: SimTransform, UNCONDITIONALLY.
            //
            // 📐 MEASURED 2026-09-13 — every production template declares exactly this and nothing else:
            //   NedTkbBuilder.DefineVehicle (BdcTkbBuilder.cs:44, every vehicle) ·
            //   BdcTkbCatalog.cs:247 (area) · :255 (route) · UrbanCombatTkbCatalog x5.
            //   ⇒ 8 sites, zero variation, and HrotEnvironmentTests' rail is literally named
            //   CreateTkb_EveryTemplateDeclaresSimTransformBirthCritical.
            //
            // ⛔⛔ AND IT IS DELIBERATELY NOT DERIVED FROM A DESCRIPTOR, which was this session's first
            //   (wrong) instinct. SimTransform reaches an entity by TWO routes:
            //     1. SpatialCoreTkbTranslator.cs:24 — stamps it when the template carries TkbMasterDto;
            //     2. NetworkSpawningSystem — cmd.InitialTransform, available to ANY template.
            //   📌 Route 2 is how the area and route templates get a position: they carry NO TkbMasterDto
            //   at all, yet BdcTkbCatalog.cs:243-246 declares birth-critical for them and says why
            //   ("ScenarioSpawnAdapter sets InitialTransform from the anchor"). ⇒ a HasDescriptor
            //   predicate would MISS them, which is the one direction that is unsafe.
            //
            // ⭐ Over-declaring costs nothing by construction: the create leg intersects this set with the
            //   entity's LIVE component mask (NetworkSpawningSystem.cs:237), so naming a component the
            //   entity never receives contributes no bits. ⛔ UNDER-declaring is the silent, dangerous
            //   direction — see TkbTemplate.BirthCriticalComponents for what breaks.
            template.AddBirthCriticalComponent<SimTransform>();

            // ⛔⛔⛔ MANDATORY COMPONENTS ARE DELIBERATELY NOT SET HERE — CE-265.
            //
            // 📐 MEASURED 2026-09-13, and the measurement is the reason: there is NO derivable rule,
            //   because the two programmatic catalogues already DISAGREE for identically-shaped templates.
            //     · NedTkbBuilder.DefineVehicle (BdcTkbBuilder.cs:36,38) — TkbMasterDto + EntityInfo(hard)
            //       + SimTransform(hard)
            //     · UrbanCombatTkbCatalog x5 — TkbMasterDto, VehicleParametersDto, BehaviorProfileDto …
            //       and NO mandatory components at all
            //     · BdcTkbCatalog area/route — no descriptors, no mandatory components
            //   ⇒ the same descriptor set yields different answers, so no predicate over the file's
            //   contents reproduces it AS THE CATALOGUES STAND.
            //
            // ⚠⚠ BUT DO NOT READ THAT AS "per-template policy was intended" — that is an inference, and it
            //   is NOT established. 📐 What is measured: HrotEnvironment.CreateTkb() registers BOTH
            //   catalogues into ONE database (:35, :39), so the disagreement is live in one cluster in
            //   front of one GhostPromotionSystem — and no design record says which answer is right.
            //   ⇒ the prior question is "is it meant to vary at all?". If UrbanCombat's omission is DRIFT,
            //   making the catalogues agree turns this into a convention like the one above. CE-265.
            //
            // 🔴 AND GUESSING IS THE WORSE FAILURE. MandatoryComponents is the PROMOTION GATE
            //   (GhostPromotionSystem: a HARD requirement that never arrives means `return`, every frame,
            //   forever). ⇒ inventing a rule here risks ghosts that NEVER PROMOTE, which is louder and
            //   worse than the status quo of an empty list. Leaving it empty preserves exactly today's
            //   file-path behaviour.
            //
            // ⚠ The status quo is NOT harmless either, which is why CE-265 exists rather than nothing:
            //   an empty list means the ghost promotes on the first frame it exists, and the P3 promote-leg
            //   role claim (GhostPromotionSystem.cs:261) fires ONCE at promotion — the entity then leaves
            //   _readyGhostQuery for good. ⇒ components that arrive afterwards are never claimed.
        }
    }
}
