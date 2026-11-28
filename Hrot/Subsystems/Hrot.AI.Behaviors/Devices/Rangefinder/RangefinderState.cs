using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Devices.Rangefinder
{
    /// <summary>The rangefinder's measuring mode (the HUD mockup's SINGLE / CONTINUOUS).</summary>
    public enum RangefinderMode : byte { Single = 0, Continuous = 1 }

    /// <summary>The rangefinder's laser power (D-74; the mockup's POWER LOW / HIGH).</summary>
    public enum RangefinderPower : byte { Low = 0, High = 1 }

    /// <summary>The menu's items (the mockup's MODE / POWER / EXIT).</summary>
    public enum RangefinderMenuItem : byte { Mode = 0, Power = 1, Exit = 2 }

    /// <summary>
    /// ⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1 SD-2′, D-57) — the RANGEFINDER's state, a
    /// <c>[UnitMemory]</c> struct in the DEVICE entity's blackboard store: survives a restart of its run (a finish, a fault, a relayout), is
    /// recorded, and is what the HUD projector (S1d) and the scenario's <c>field</c> cue (<c>memory: RangefinderState</c>) read. Written ONLY
    /// by the device's own logic (<see cref="RangefinderBehavior.Tick"/>) — the parallel rule.
    /// </summary>
    [UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public struct RangefinderState
    {
        public RangefinderMode Mode;
        public RangefinderPower Power;
        /// <summary>1 while the menu is open.</summary>
        public byte MenuOpen;
        /// <summary>The menu's cursor while open.</summary>
        public RangefinderMenuItem Menu;
        /// <summary>1 while the laser is on (a measurement asked and not answered, or CONTINUOUS ranging).</summary>
        public byte Ranging;
        /// <summary>1 once a range was measured (a return); 0 = "— m" (never measured, or the last one had no return).</summary>
        public byte HasRange;
        /// <summary>1 when the last range was measured on a paused frame (a <c>Preview</c> spot — the HUD shows it, nothing acts on it).</summary>
        public byte RangePreview;
        /// <summary>The last measured range (m); valid when <see cref="HasRange"/>.</summary>
        public float LastRange;
        /// <summary>The body the last range met (this node's handle; null on terrain).</summary>
        public Entity LastHit;
        /// <summary>The measurement request number (<c>LaserPointing.Seq</c>) — bumped per RANGE.</summary>
        public uint Seq;
        /// <summary>⭐ R7-11 — the press counters last seen and the operator epoch they are based on (⭐ S1d-3: the ONE tracker every device keeps,
        /// read by <c>DevicePressReader</c>).</summary>
        public Fdp.Toolkit.Behavior.Devices.DevicePressTracker Presses;
    }

    /// <summary>
    /// The rangefinder's press channels (the <c>DeviceInput</c> press counter each one reads) — the BUTTON indexes of TKB type 640's elements (D-142:
    /// RANGE, MODE, MENU, SELECT; pinned by <c>RangefinderBehaviorTests.S1e1_…</c>); the numbers are the wire, the names are the type's data.
    /// </summary>
    public static class RangefinderChannels
    {
        /// <summary>The channels the rangefinder reads.</summary>
        public const int Count = 4;
        /// <summary>RANGE — SINGLE: one measurement; CONTINUOUS: ranging on / off.</summary>
        public const int Range = 0;
        /// <summary>MODE — steps SINGLE ↔ CONTINUOUS.</summary>
        public const int Mode = 1;
        /// <summary>MENU — opens the menu, then steps its cursor MODE → POWER → EXIT.</summary>
        public const int Menu = 2;
        /// <summary>SELECT — applies the cursor's item (MODE steps the mode, POWER the power, EXIT closes the menu).</summary>
        public const int Select = 3;
    }

    /// <summary>The rangefinder's panel channels (<c>DevicePanel</c>) — the same placeholder rule as <see cref="RangefinderChannels"/>.</summary>
    public static class RangefinderPanel
    {
        /// <summary>Lamp bits.</summary>
        public const uint LampLaser = 1u << 0, LampRange = 1u << 1, LampMenu = 1u << 2, LampPreview = 1u << 3;
        /// <summary>Value slots: the range (m, 0 = none), the mode, the power, the menu cursor (+1; 0 = closed).</summary>
        public const int ValueRange = 0, ValueMode = 1, ValuePower = 2, ValueMenu = 3;
        /// <summary>Text lines: the mode, the range ("452 m" / "— m"), the menu line, the power.</summary>
        public const int TextMode = 0, TextRange = 1, TextMenu = 2, TextPower = 3;
    }
}
