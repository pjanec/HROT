using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Hrot.AI.Behaviors.Brains;   // the StatefulAction builder extension
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Devices.Rangefinder
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1 D-8 / D-57, §3.5, §3.7, §3.8) — THE LASER RANGEFINDER's
    /// logic: ONE stateful action on the DEVICE entity, run every frame (paused or not) by the device tick (<c>DeviceTickSystem</c>), which starts it
    /// from <c>DeviceInfo.Behavior</c> and restarts it if it ever ends. Its state is <see cref="RangefinderState"/> (unit memory).
    /// <para>Each tick: ① the PRESSES — a press is a <c>DeviceInput</c> press counter that moved since the last one seen; a new operator epoch
    /// (<c>PossessedBy.Epoch</c>, R7-11), or input appearing after none, RE-BASES the counters so no press fires; a re-read frame never presses twice.
    /// ② MODE / MENU / SELECT step the mode and the menu. ③ RANGE starts a measurement (a new <c>LaserPointing.Seq</c>, the laser on); the spot that
    /// answers THAT seq (<c>LaserSpot.PointingSeq</c>, written by the muscle a hop later) gives <see cref="RangefinderState.LastRange"/> — and turns the
    /// laser off in SINGLE (CONTINUOUS keeps ranging until RANGE again). ④ <c>LaserPointing</c> and <c>DevicePanel</c> are written only on change.</para>
    /// <para>⭐ The parallel rule: it writes ONLY its own entity (its memory, its pointing, its panel) and reads only its own components. ⭐ Pause
    /// (R-4300, R6-4): it still answers presses — pause stops the WORLD, not the device's own state; a range measured in pause is a preview.
    /// 0 B per steady tick (R-2084): the text is formatted into stack spans.</para>
    /// </summary>
    public static class RangefinderBehavior
    {
        /// <summary>The power each mode emits (relative, <c>LaserPointing.Power</c>).</summary>
        public const float LowPower = 0.3f, HighPower = 1f;

        /// <summary>The laser's band: near infra-red (the visibility rule's key, SD-S6).</summary>
        public const byte Band = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct RangefinderParams
        {
            /// <summary>Output at POWER LOW / HIGH (0..1).</summary>
            public float LowPower, HighPower;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RangefinderWork
        {
            /// <summary>Ticks of this run (diagnostic).</summary>
            public uint Ticks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RangefinderBlackboard
        {
            public RangefinderParams Params;
            public RangefinderWork Work;
        }

        [BehaviorResolver(BehaviorNames.DeviceRangefinder)]
        public static void Resolve(in NoParams authored, ref RangefinderBlackboard block, EntityRepository world, Entity self)
            => block = new RangefinderBlackboard { Params = new RangefinderParams { LowPower = LowPower, HighPower = HighPower } };

        // ⛔ Offered to nothing but a device: Requires DeviceInfo (BehaviorFit's AND), so no unit's task picker lists it.
        [Behavior(BehaviorNames.DeviceRangefinder, Params = typeof(NoParams), Requires = new[] { typeof(DeviceInfo) },
                  Description = "A device's logic: the laser rangefinder (run by the device tick, never assigned).")]
        [BTreeDefinition(BehaviorNames.DeviceRangefinder, Curated = true, Group = "Library/Devices", ParamsType = typeof(RangefinderBlackboard))]
        public static BTreeBuilder<RangefinderBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<RangefinderBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.Work, Tick);

        /// <summary>One tick of the rangefinder (see the class summary). Never ends: Running.</summary>
        [SharedAiAction]
        public static NodeStatus Tick(ref RangefinderParams p, ref RangefinderWork ws, Entity self, EntityRepository world)
        {
            ws.Ticks++;
            ref var st = ref UnitMemory.Ref<RangefinderState>(world, self);
            Presses(world, self, ref st, out int range, out int mode, out int menu, out int select);

            // ② the mode and the menu
            if ((mode & 1) != 0) st.Mode = st.Mode == RangefinderMode.Single ? RangefinderMode.Continuous : RangefinderMode.Single;
            for (int i = 0; i < menu; i++)
            {
                if (st.MenuOpen == 0) { st.MenuOpen = 1; st.Menu = RangefinderMenuItem.Mode; }
                else st.Menu = (RangefinderMenuItem)(((int)st.Menu + 1) % 3);
            }
            for (int i = 0; i < select && st.MenuOpen != 0; i++)
            {
                switch (st.Menu)
                {
                    case RangefinderMenuItem.Mode: st.Mode = st.Mode == RangefinderMode.Single ? RangefinderMode.Continuous : RangefinderMode.Single; break;
                    case RangefinderMenuItem.Power: st.Power = st.Power == RangefinderPower.Low ? RangefinderPower.High : RangefinderPower.Low; break;
                    default: st.MenuOpen = 0; st.Menu = RangefinderMenuItem.Mode; break;
                }
            }

            // ③ ranging — the answer to the CURRENT request only (a spot left from an earlier emission carries an older seq)
            if (st.Ranging != 0 && world.IsComponentTypeRegistered<LaserSpot>() && world.HasComponent<LaserSpot>(self))
            {
                ref readonly var spot = ref world.GetComponentRO<LaserSpot>(self);
                if (spot.PointingSeq == st.Seq)
                {
                    st.HasRange = spot.Count > 0 ? (byte)1 : (byte)0;
                    st.LastRange = spot.Count > 0 ? spot.Points[0].Range : 0f;
                    st.LastHit = spot.Count > 0 ? spot.Points[0].Hit : Entity.Null;
                    st.RangePreview = spot.Preview;
                    if (st.Mode == RangefinderMode.Single) st.Ranging = 0;   // one measurement: the laser goes off
                }
            }
            if (range > 0)
            {
                if (st.Mode == RangefinderMode.Continuous && st.Ranging != 0) st.Ranging = 0;   // CONTINUOUS: RANGE again stops
                else { st.Seq++; st.Ranging = 1; }
            }

            // ④ its own outputs, on change
            WritePointing(world, self, in st, p.LowPower, p.HighPower);
            WritePanel(world, self, in st);
            return NodeStatus.Running;
        }

        /// <summary>① The presses since the last tick, per channel; re-bases (no press) on a new operator epoch or on input appearing.</summary>
        private static void Presses(EntityRepository world, Entity self, ref RangefinderState st, out int range, out int mode, out int menu, out int select)
        {
            range = mode = menu = select = 0;
            uint epoch = world.IsComponentTypeRegistered<PossessedBy>() && world.HasComponent<PossessedBy>(self)
                         && world.GetComponentRO<PossessedBy>(self).Mode == PossessionMode.Operated
                ? world.GetComponentRO<PossessedBy>(self).Epoch : 0u;
            if (!world.IsComponentTypeRegistered<DeviceInput>() || !world.HasComponent<DeviceInput>(self)) { st.Based = 0; st.Epoch = epoch; return; }
            ref readonly var presses = ref world.GetComponentRO<DeviceInput>(self).Control.Presses;
            byte r = presses[RangefinderChannels.Range], m = presses[RangefinderChannels.Mode], n = presses[RangefinderChannels.Menu], s = presses[RangefinderChannels.Select];
            if (st.Based != 0 && st.Epoch == epoch)
            {
                range = (byte)(r - st.SeenRange); mode = (byte)(m - st.SeenMode); menu = (byte)(n - st.SeenMenu); select = (byte)(s - st.SeenSelect);
            }
            st.SeenRange = r; st.SeenMode = m; st.SeenMenu = n; st.SeenSelect = s;
            st.Based = 1; st.Epoch = epoch;
        }

        private static void WritePointing(EntityRepository world, Entity self, in RangefinderState st, float low, float high)
        {
            if (!world.IsComponentTypeRegistered<LaserPointing>()) return;
            var next = new LaserPointing
            {
                On = st.Ranging, Band = Band, Power = st.Power == RangefinderPower.High ? high : low, Direction = default, Seq = st.Seq,
            };
            if (world.HasComponent<LaserPointing>(self))
            {
                ref readonly var now = ref world.GetComponentRO<LaserPointing>(self);
                if (now.On == next.On && now.Band == next.Band && now.Power == next.Power && now.Direction == next.Direction && now.Seq == next.Seq) return;
                world.SetComponent(self, next);
            }
            else world.AddComponent(self, next);   // added WITH its value (the parallel rule)
        }

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static void WritePanel(EntityRepository world, Entity self, in RangefinderState st)
        {
            if (!world.IsComponentTypeRegistered<DevicePanel>()) return;
            var panel = new DevicePanel();
            panel.LampBits = (st.Ranging != 0 ? RangefinderPanel.LampLaser : 0u) | (st.HasRange != 0 ? RangefinderPanel.LampRange : 0u)
                             | (st.MenuOpen != 0 ? RangefinderPanel.LampMenu : 0u) | (st.HasRange != 0 && st.RangePreview != 0 ? RangefinderPanel.LampPreview : 0u);
            panel.Values[RangefinderPanel.ValueRange] = st.HasRange != 0 ? st.LastRange : 0f;
            panel.Values[RangefinderPanel.ValueMode] = (float)st.Mode;
            panel.Values[RangefinderPanel.ValuePower] = (float)st.Power;
            panel.Values[RangefinderPanel.ValueMenu] = st.MenuOpen != 0 ? (float)st.Menu + 1f : 0f;
            panel.Text[RangefinderPanel.TextMode] = new FixedString32(st.Mode == RangefinderMode.Single ? "SINGLE" : "CONTINUOUS");
            panel.Text[RangefinderPanel.TextPower] = new FixedString32(st.Power == RangefinderPower.High ? "POWER HIGH" : "POWER LOW");
            panel.Text[RangefinderPanel.TextMenu] = st.MenuOpen == 0 ? default
                : new FixedString32(st.Menu == RangefinderMenuItem.Mode ? "MENU ▸ MODE" : st.Menu == RangefinderMenuItem.Power ? "MENU ▸ POWER" : "MENU ▸ EXIT");
            Span<char> text = stackalloc char[24];
            int len;
            if (st.HasRange != 0 && ((int)MathF.Round(st.LastRange)).TryFormat(text, out len, default, Invariant)) { text[len++] = ' '; text[len++] = 'm'; }
            else { text[0] = '—'; text[1] = ' '; text[2] = 'm'; len = 3; }
            panel.Text[RangefinderPanel.TextRange] = new FixedString32(text[..len]);
            DevicePanels.Write(world, self, in panel);
        }
    }
}
