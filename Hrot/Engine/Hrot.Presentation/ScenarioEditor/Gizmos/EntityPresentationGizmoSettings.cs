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
        /// ⭐ <b>Default OFF — it preserves what every host renders today.</b>
        ///
        /// <para>⚠⚠ <b>CORRECTED <c>2026-08-30</c>. An earlier version of this comment claimed the default
        /// was MEASURED: that enabling culling "blanked the IG perspective" in a live run. 🔴 That was a
        /// FIRST-PROBE SETTLING ARTIFACT, and it is refuted.</b> Re-measured with culling forced ON, both
        /// WITH and WITHOUT a fix to the culling input: IG reads <c>SpatialAnchor 0</c> on the first probe
        /// after a scenario load and <c>8</c> on every probe after — identically in both builds. Culling
        /// does not blank IG. See <c>CE-131</c> (refuted) and <c>UX_Feature_Map_Parity.md</c> §3.2g.</para>
        ///
        /// <para>⇒ <b>OFF is still the right default, for a plainer reason:</b> before the merge, IG's map
        /// was drawn by SimHost's and CGF's projectors, which ignored culling — so OFF reproduces the
        /// behaviour every host actually had. ⛔ It is a compatibility choice, not a defect workaround.
        /// Turning it ON is a deliberate per-host act; a host with a real viewport culls correctly.</para>
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
