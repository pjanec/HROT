using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    internal static class HillAttackGizmoSettings
    {
        public const string ShowSlots = "HillAttack.ShowSlots";

        public static readonly GizmoSettingValue DefaultShowSlots = GizmoSettingValue.From(true);

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(ShowSlots, DefaultShowSlots);
        }
    }
}
