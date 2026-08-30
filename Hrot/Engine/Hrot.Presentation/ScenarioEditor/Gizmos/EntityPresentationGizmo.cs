using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

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

            // ⭐ SimTransform is the single source on every host. CGF's copy preferred NetworkTransform on
            // the grounds that it is "fresher on a host that does not own the entity" — measured false:
            // GeoSpatialIngressTranslator writes BOTH from the same packet in the same call (:75, :89), so
            // on a non-owner they are identical, and on an OWNER NetworkTransform is the last-PUBLISHED
            // shadow while SimTransform is live. The preference was never better and was sometimes stale.
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

            EntityPresentationGizmoShared.DrawSpatialAnchorFromRotation(draw, networkId, tf.Position, tf.Rotation);

            // ⭐ CGF's copy omitted the pick box, so CGF entities could not be picked at all (CE-126b).
            EntityPresentationGizmoShared.EmitPickBox(draw, entity, networkId, tf.Position);

            // ── Condition: IG's damage states, now available to every host that produces IgHealthState. ──
            uint conditionMask = 0u;
            if (view.HasComponent<IgHealthState>(entity))
            {
                ref readonly var health = ref view.GetComponentRO<IgHealthState>(entity);
                if (health.Damage >= DamagedThreshold)  conditionMask |= ConditionDamaged;
                if (health.Damage >= ImmobileThreshold) conditionMask |= ConditionImmobile;
            }

            EntityPresentationGizmoShared.TryGetVehicleDimensions(view, entity, out float length, out float width);
            ulong profileId = EntityPresentationGizmoShared.ResolveProfileId(view, entity);

            // ⭐ Through the shared helper, which forces an opaque colour. CGF's copy called the raw
            // builder, which starts from default(DebugPrimitive) and leaves Color at (0,0,0,0) — so CGF's
            // avatars were emitted fully transparent (CE-126a).
            EntityPresentationGizmoShared.DrawSemanticShape(
                draw,
                entity,
                networkId,
                profileId,
                length,
                width,
                conditionMask);
        }
    }
}
