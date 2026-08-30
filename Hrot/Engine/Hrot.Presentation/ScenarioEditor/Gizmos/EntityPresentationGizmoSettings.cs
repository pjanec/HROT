using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐⭐ <b>The configuration surface of <see cref="EntityPresentationGizmo"/> — <c>UXI-23</c> <c>S2</c>.</b>
    ///
    /// <para>🔒 <b>This is <c>R-137</c> made concrete</b> — <i>"if unification takes a feature away, that is a
    /// signal to think about how to put it back, via configuration for example."</i> IG's off-screen culling
    /// and its damage thresholds were host-private constants inside one of three copies. Unifying could
    /// either drop them (losing a feature) or hard-wire them (imposing one host's behaviour on all five).
    /// Neither is acceptable, so they become SETTINGS: every host can turn them on, and the default is the
    /// behaviour every host actually renders today.</para>
    /// </summary>
    public static class EntityPresentationGizmoSettings
    {
        // ── culling ────────────────────────────────────────────────────────────────────────────

        /// <summary>Skip entities whose <c>CullingState</c> says they are off-screen.</summary>
        public const string CullOffscreen = "map.entity.cullOffscreen";

        /// <summary>
        /// 🔴🔴 <b>Default OFF, and that default is MEASURED, not chosen for safety.</b>
        ///
        /// <para>📐 Live on <c>--mode all</c>, <c>2026-08-30</c>: with culling on by default, the IG
        /// perspective emitted <b>ZERO</b> <c>SpatialAnchor</c> and <c>SemanticShape</c> primitives —
        /// down from 16 of each. <c>MapCullingSystem</c> writes <c>IsVisible</c> from
        /// <c>MapCameraViewport</c>, which <c>IgApplication.cs:963</c> fills from the <b>projected screen
        /// corners</b> of the live map view. With no real viewport that rectangle is degenerate, every
        /// entity tests out of view, and the map goes blank.</para>
        ///
        /// <para>⚠⚠ <b>Before the merge this was INVISIBLE</b>, and that is the important part.
        /// <c>IgEntityPresentationGizmo</c> did cull — and drew nothing — but SimHost's and CGF's copies
        /// ignored culling entirely, so IG's map was in fact rendered by the two OTHER hosts' projectors.
        /// The 16 anchors were 2 × 8, from the two non-culling copies. Merging removed the copies that
        /// were doing the work, which is what surfaced a culling input that has been wrong all along.</para>
        ///
        /// <para>⇒ <b>OFF preserves exactly what every host renders today</b>, and turning it ON is now a
        /// deliberate, per-host act rather than an accident of which copy ran. The culling INPUT is a
        /// separate defect, filed as <c>CE-131</c>; when it is fixed a host may enable this and get the
        /// performance benefit the system was written for.</para>
        /// </summary>
        public static readonly GizmoSettingValue DefaultCullOffscreen = GizmoSettingValue.From(false);

        // ── condition mask ─────────────────────────────────────────────────────────────────────

        /// <summary>Damage at or above which the semantic shape is drawn damaged.</summary>
        public const string DamagedThreshold = "map.entity.damagedThreshold";

        /// <summary>Damage at or above which the semantic shape is drawn immobile.</summary>
        public const string ImmobileThreshold = "map.entity.immobileThreshold";

        /// <summary>⭐ IG's original constants, now the shared defaults rather than one host's literals.</summary>
        public static readonly GizmoSettingValue DefaultDamagedThreshold  = GizmoSettingValue.From(50f);

        /// <inheritdoc cref="DefaultDamagedThreshold"/>
        public static readonly GizmoSettingValue DefaultImmobileThreshold = GizmoSettingValue.From(90f);

        public static void Register(GizmoSettingsRegistry settings)
        {
            if (settings == null) return;

            settings.RegisterSetting(CullOffscreen,     DefaultCullOffscreen);
            settings.RegisterSetting(DamagedThreshold,  DefaultDamagedThreshold);
            settings.RegisterSetting(ImmobileThreshold, DefaultImmobileThreshold);
        }
    }
}
