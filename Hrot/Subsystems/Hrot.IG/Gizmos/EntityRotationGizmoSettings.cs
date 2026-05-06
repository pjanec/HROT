using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    internal static class EntityRotationGizmoSettings
    {
        public const string ArrowLength = "EntityRotation.ArrowLength";

        public static readonly GizmoSettingValue DefaultArrowLength = GizmoSettingValue.From(30f); // meters

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(ArrowLength, DefaultArrowLength);
        }
    }
}
