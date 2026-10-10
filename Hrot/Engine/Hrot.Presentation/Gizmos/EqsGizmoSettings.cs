using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Setting key strings for EQS diagnostic gizmos.
    // Keys are hashed to uint at construction time via GizmoSettingsRegistry.ComputeHash.
    // ⭐ CE-3123 — moved with EqsSensorGizmo out of the Hrot.IG assembly; public so Hrot.IG.Tests still reaches it.
    public static class EqsGizmoSettings
    {
        // Setting key strings -- must be stable (hashed into uint at construction time)
        public const string ShowRadius     = "EQS.ShowSearchRadius";
        public const string ShowCandidates = "EQS.ShowTopKCandidates";
        public const string ShowScores     = "EQS.ShowScores";
        /// <summary>⭐ CE-3143 — every generated candidate, green when the filters keep it, red when they reject it.</summary>
        public const string ShowVerdict    = "EQS.ShowVerdict";

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(ShowRadius,     GizmoSettingValue.From(true));
            settings.RegisterSetting(ShowCandidates, GizmoSettingValue.From(true));
            settings.RegisterSetting(ShowScores,     GizmoSettingValue.From(true));
            settings.RegisterSetting(ShowVerdict,    GizmoSettingValue.From(true));
        }
    }
}
