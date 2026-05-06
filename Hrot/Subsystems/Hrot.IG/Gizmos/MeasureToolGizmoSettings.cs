using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    internal static class MeasureToolGizmoSettings
    {
        /// <summary>Bool: true = measure tool is active on the canvas.</summary>
        public const string Active = "MeasureTool.Active";

        /// <summary>Int: 0 = meters, 1 = kilometers.</summary>
        public const string Units = "MeasureTool.Units";

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(Active, GizmoSettingValue.From(false));
            settings.RegisterSetting(Units,  GizmoSettingValue.From(0));
        }
    }
}
