using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.7, D-28 / D-29) — a device's GENERIC panel outputs: lamp
    /// bits, blink bits, sixteen values and four short text lines. The wire is the same for every device; what each channel MEANS is the
    /// device type's data (lamp 0 = "LASER" on a rangefinder). Written by the device's own logic ONLY ON CHANGE (<see cref="DevicePanels.Write"/>
    /// bumps <see cref="Revision"/>), so replication — change-detected per component — sends it on change and a late joiner gets it for free.
    /// Unmanaged, recorded. ⛔ Not the HUD: the HUD is gizmos drawn from the device's state (D-30); this is what a replica panel shows.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DevicePanel)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct DevicePanel
    {
        /// <summary>Bumped on every change (a panel adapter compares it).</summary>
        public uint Revision;
        public uint LampBits;
        public uint BlinkBits;
        public DevicePanelValues Values;
        public DevicePanelTexts Text;

        /// <summary>True when the OUTPUTS equal (the revision is not an output).</summary>
        public static bool SameOutputs(in DevicePanel a, in DevicePanel b)
        {
            if (a.LampBits != b.LampBits || a.BlinkBits != b.BlinkBits) return false;
            for (int i = 0; i < DevicePanelValues.Count; i++)
                if (BitConverterSingle(a.Values[i]) != BitConverterSingle(b.Values[i])) return false;
            for (int i = 0; i < DevicePanelTexts.Count; i++)
                if (!a.Text[i].Equals(b.Text[i])) return false;
            return true;
        }

        private static int BitConverterSingle(float v) => Unsafe.As<float, int>(ref v);
    }

    /// <summary>Sixteen panel values (a range, a battery level, a zoom step — the type names them).</summary>
    [InlineArray(Count)]
    public struct DevicePanelValues
    {
        public const int Count = 16;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private float _element;
#pragma warning restore IDE0044
    }

    /// <summary>Four short text lines (31 UTF-8 bytes each).</summary>
    [InlineArray(Count)]
    public struct DevicePanelTexts
    {
        public const int Count = 4;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private FixedString32 _element;
#pragma warning restore IDE0044
    }

    /// <summary>⭐ The ONE writer routine of a device's panel (D-28): writes only when an output changed, bumping the revision.</summary>
    public static class DevicePanels
    {
        /// <summary>Writes <paramref name="next"/>'s outputs to <paramref name="device"/>'s panel when they differ from what it shows (adding the
        /// panel WITH its value when absent — the parallel rule); returns true when it wrote. 0 B.</summary>
        public static bool Write(EntityRepository w, Entity device, in DevicePanel next)
        {
            if (!w.IsComponentTypeRegistered<DevicePanel>()) return false;
            if (w.HasComponent<DevicePanel>(device))
            {
                ref readonly var now = ref w.GetComponentRO<DevicePanel>(device);
                if (DevicePanel.SameOutputs(in now, in next)) return false;
                var changed = next;
                changed.Revision = now.Revision + 1;
                w.SetComponent(device, changed);
                return true;
            }
            var first = next;
            first.Revision = 1;
            w.AddComponent(device, first);
            return true;
        }
    }
}
