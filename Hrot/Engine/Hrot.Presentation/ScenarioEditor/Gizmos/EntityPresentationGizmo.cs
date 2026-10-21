using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Fdp.Toolkit.Combat.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// <b>UXI-23 S2 — the ONE entity presentation projector.</b> Replaces the three host-private copies
    /// (<c>IgEntityPresentationGizmo</c> · <c>SimHostEntityPresentationGizmo</c> ·
    /// <c>CgfEntityPresentationGizmo</c>), which already called the same
    /// <see cref="EntityPresentationGizmoShared"/> helpers and differed only in their INPUTS and in two
    /// defects. 📄 The design, the line-by-line comparison and the capability ledger:
    /// <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.9c and §3.9j.
    ///
    /// <para><b>⛔ The query is <c>SimTransform</c> + <c>NetworkIdentity</c> and nothing else</b>, and that
    /// is load-bearing. IG's copy also required <c>CullingState</c>; keeping it here would make the mask
    /// match NOTHING on SimHost and CGF — which produce no <c>CullingState</c> — and silently empty their
    /// maps. A <c>[GizmoProjector]</c> requirement is a hard filter, never an optional input.</para>
    ///
    /// <para><b>⭐ The per-host behaviours are CONFIGURATION, not forks.</b> 🔒 <c>R-137</c>: unification
    /// may not cost a feature; where it would, the feature comes back as a setting. IG's damage thresholds
    /// live in <see cref="EntityPresentationGizmoSettings"/>, and the mask additionally follows the
    /// <c>ST-031</c> rule (<i>"support all and decide on current presence of component"</i>), costing
    /// nothing on a host that carries no health component.</para>
    ///
    /// <para>⚠⚠ <b>Culling is NOT here — <c>S4</c> moved it to
    /// <c>Hrot.ScenarioEditor.Map.CullingStateVisibilityPolicy</c>.</b> <c>S2a</c> had it inline in
    /// <see cref="Draw"/>, which made two implementations of <i>"should this entity draw?"</i> — the other
    /// being <c>IGizmoVisibilityPolicy</c>, which <c>StatelessGizmoSystem</c> now honours per entity.
    /// Ruling 9 allows one. 📄 <c>UX_Feature_Map_Parity.md</c> §3.2f ·
    /// <c>UX_Feature_Entity_Symbology.md</c> §3.4.</para>
    /// </summary>
    [GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
    public sealed class EntityPresentationGizmo : IStatelessGizmo
    {
        private const uint ConditionDamaged  = 1u << 0;
        private const uint ConditionImmobile = 1u << 1;
        /// <summary>⭐ W2 (docs/DESIGN_Visual_Effects.md §7 VE-O) — the MIL-STD-2525 X (<c>EntityShapeCondition.Destroyed</c>).</summary>
        private const uint ConditionDestroyed = 1u << 2;

        private readonly GizmoSettingsRegistry? _settings;
        private readonly uint _damagedKey;
        private readonly uint _immobileKey;

        /// <summary>
        /// Parameterless construction keeps every setting at its default — the shape a unit test wants,
        /// and the shape <c>GizmoReflectionRegistrar</c> falls back to when no settings registry exists.
        /// </summary>
        public EntityPresentationGizmo() : this(null) { }

        /// <summary>
        /// ⭐ <c>GizmoReflectionRegistrar.Instantiate</c> prefers this overload whenever the host has a
        /// <see cref="GizmoSettingsRegistry"/>, so a host configures the map by writing settings rather
        /// than by owning a projector.
        /// </summary>
        public EntityPresentationGizmo(GizmoSettingsRegistry? settings)
        {
            _settings = settings;
            EntityPresentationGizmoSettings.Register(settings!);

            _damagedKey  = GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.DamagedThreshold);
            _immobileKey = GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.ImmobileThreshold);
        }

        private float DamagedThreshold
            => _settings != null
                ? _settings.Read(_damagedKey).FloatValue
                : EntityPresentationGizmoSettings.DefaultDamagedThreshold.FloatValue;

        private float ImmobileThreshold
            => _settings != null
                ? _settings.Read(_immobileKey).FloatValue
                : EntityPresentationGizmoSettings.DefaultImmobileThreshold.FloatValue;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            // ── Culling lives in CullingStateVisibilityPolicy, not here (S4, ruling 9). ──
            // S2a had the setting check and the CullingState presence test inline in this method. That
            // was a second implementation of "should this entity draw?" alongside IGizmoVisibilityPolicy,
            // which StatelessGizmoSystem now honours per entity. The logic moved wholesale; this projector
            // is back to one job — emitting primitives. 📄 UX_Feature_Map_Parity.md §3.2f.

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;

            // ⭐⭐⭐ §6.8 — the projector attribute guarantees the component is PRESENT, never that its
            //   value is usable. ⛔ With 0 this gizmo emits a SpatialAnchor nothing can reference, a pick
            //   box that swallows clicks, and an EntityLocal SemanticShape that resolves against no
            //   anchor — three broken primitives from one unset field. ⚠ An entity whose id has not been
            //   allocated yet is a NORMAL transient state, so returning is correct: it gets its avatar on
            //   the frame the id exists. 📌 Prior art: ContextMenuProjectorGizmo.cs:102.
            if (networkId == 0) return;

            // ⭐ CE-1033 S5 (R-260) the map CAMERA, ⭐ LR-3 (R-271) a replicated ROUND, ⭐ CE-1165 a gas / smoke CLOUD — each drawn by its own
            //   layer, not a thing on the map: no symbol, no anchor, no pick box (the one rule, Hrot.Common NotAUnit).
            if (Hrot.Common.Diagnostics.Gizmos.NotAUnit.Is(view, entity)) return;
            // ⭐ CE-1058 — a passenger inside an ENCLOSED vehicle has no symbol, anchor or pick box of its own (the vehicle's is
            //   the one on the map); an exposed rider keeps them. The engine's one rule (PhysicalPresence, R-271).
            if (Fdp.Toolkit.Perception.PhysicalPresence.IsEnclosedPassenger(view, entity)) return;

            // ⭐ SimTransform is the single source on every host. CGF's copy preferred NetworkTransform on
            // the grounds that it is "fresher on a host that does not own the entity" — measured false:
            // GeoSpatialIngressTranslator writes BOTH from the same packet in the same call (:75, :89), so
            // on a non-owner they are identical, and on an OWNER NetworkTransform is the last-PUBLISHED
            // shadow while SimTransform is live. The preference was never better and was sometimes stale.
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

            EntityPresentationGizmoShared.DrawSpatialAnchorFromRotation(draw, networkId, tf.Position, tf.Rotation);

            // ⭐ CE-1232 (docs/DESIGN_Visual_Effects.md §11 CH-2) — a placed charge is drawn as the IED marker, a fixed size on screen
            ulong profileId = EntityPresentationGizmoShared.ResolveProfileId(view, entity);
            bool placedCharge = ChargeMarker.IsPlaced(view, entity, profileId);

            // ⭐ CGF's copy omitted the pick box, so CGF entities could not be picked at all (CE-126b).
            // ⭐ CE-3147 — sized from the entity's own footprint, so the clickable area tracks the drawn symbol (the marker's, CE-1232).
            if (placedCharge)
                EntityPresentationGizmoShared.EmitPickBox(draw, networkId, tf.Position, ChargeMarker.HalfPixels + 2f, sizeMode: SizeMode.ScreenPixels);
            else
                EntityPresentationGizmoShared.EmitPickBox(draw, networkId, tf.Position,
                    Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.InteractionRadiusMetres(view, entity));

            // ── Condition: the damage states, now read off the replicated Health itself. ──
            // ⭐ CE-196 — this used to read IgHealthState.Damage, a precomputed percentage. The
            //   thresholds are still expressed as PERCENTAGES (DamagedThreshold / ImmobileThreshold),
            //   so the fraction is converted here rather than the constants being rescaled — the
            //   observable condition mask is unchanged.
            // ⚠ Max <= 0 yields no condition bits rather than a division by zero.
            uint conditionMask = 0u;
            if (view.HasComponent<Health>(entity))
            {
                ref readonly var health = ref view.GetComponentRO<Health>(entity);
                if (health.Max > 0f)
                {
                    float damagePct = (1f - health.Current / health.Max) * 100f;
                    if (damagePct >= DamagedThreshold)  conditionMask |= ConditionDamaged;
                    if (damagePct >= ImmobileThreshold) conditionMask |= ConditionImmobile;
                }
            }
            // ⭐ W2 (VE-N) — the one "destroyed" test, shared with the 3-D wreck and the flames.
            if (Fdp.Toolkit.Combat.CombatLife.IsDestroyed(view, entity)) conditionMask |= ConditionDestroyed;

            // ⭐ CE-1068 — a TERRAIN OBJECT (a door) is picked and selected here, but it has NO symbol: the door layer draws its leaf
            //   (2-D) and the 3-D map its slab. 📄 DESIGN_Building_Interiors.md §3j 5b: "no symbol, no palette entry". ⛔ It drew the
            //   unknown-type fallback (magenta) for every door.
            if (view.HasManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>(entity)) return;

            // ⭐ CE-1232 (docs/DESIGN_Visual_Effects.md §11 CH-2) — a placed charge is an IED marker, not a 30 cm vehicle outline
            if (placedCharge)
            {
                ChargeMarker.Draw(draw, tf.Position, Hrot.Common.Diagnostics.Gizmos.AffiliationColors.For(view, entity));
                return;
            }

            EntityPresentationGizmoShared.TryGetVehicleDimensions(view, entity, out float length, out float width);

            // ⭐ Through the shared helper, which forces an opaque colour. CGF's copy called the raw
            // builder, which starts from default(DebugPrimitive) and leaves Color at (0,0,0,0) — so CGF's
            // avatars were emitted fully transparent (CE-126a).
            EntityPresentationGizmoShared.DrawSemanticShape(
                draw,
                networkId,
                profileId,
                length,
                width,
                conditionMask,
                colour: Hrot.Common.Diagnostics.Gizmos.AffiliationColors.For(view, entity));   // ⭐ CE-1163 — the live side
        }
    }
}
