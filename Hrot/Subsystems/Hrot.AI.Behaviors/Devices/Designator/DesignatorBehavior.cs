using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Hrot.AI.Behaviors.Brains;   // the StatefulAction builder extension
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Devices.Designator
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-113 – D-115, §3.1 D-8 / D-57) — THE LASER TARGET DESIGNATOR's
    /// logic: ONE stateful action on the DEVICE entity, run every frame (paused or not) by the device tick, started from <c>DeviceInfo.Behavior</c> and restarted
    /// if it ever ends. Its state is <see cref="DesignatorState"/> (unit memory).
    /// <para>Each tick: ① the PRESSES (<see cref="DevicePressReader"/>, re-based on a new operator epoch, R7-11); ② POWER — OFF ⇄ STANDBY; ③ the DUTY CYCLE —
    /// a lase that ran <c>LaseSeconds</c> of sim time goes off and COOLS, a cooling that ran <c>CoolSeconds</c> returns to STANDBY (sim-time stamps: a pause
    /// stands them still, D-115); ④ the answer of its own spot to the CURRENT request (<c>LaserSpot.PointingSeq</c>): the range (a RANGE pulse, or continuously
    /// while lasing) and whether the spot is a live designation; ⑤ CODE steps the PRF code (STANDBY only, <see cref="DesignatorCodes"/>); ⑥ LASE — STANDBY lases
    /// on a live frame and QUEUES in a pause (R-4300: no designation in pause; the queued lase starts on the first live frame), LASING stops; ⑦ RANGE fires one
    /// uncoded pulse; ⑧ a target a linked device SENT (its inbox) into its memory — the HUD's cue; ⑨ its own outputs, on change: the look
    /// (<see cref="DeviceLooks.Follow"/>) and the laser (<see cref="LaserPointing"/> — band 2 with its code while lasing, D-114: the S1c laser, traced by the
    /// one <c>LaserSpotSystem</c>; ⛔ no second laser path).</para>
    /// <para>⭐ The parallel rule: it writes ONLY its own entity and reads only its own components. ⭐ A lase running into a pause keeps the laser on — the muscle
    /// writes its spot <c>Preview</c> (nothing homes on it) and the timer stands. 0 B per steady tick (R-2084).</para>
    /// </summary>
    public static class DesignatorBehavior
    {
        /// <summary>The type data's defaults (a device with no TKB type — a bare rail): <see cref="Fdp.Toolkit.Tkb.Domain.LaserDesignatorDto"/>'s.</summary>
        public const float DefaultLaseSeconds = 60f, DefaultCoolSeconds = 30f;
        /// <inheritdoc cref="DefaultLaseSeconds"/>
        public const ushort DefaultCode = 1688;

        /// <summary>The designator's fixed magnification (TKB 643's one step) and the field of view a view bound to it shows (<see cref="DeviceOptics"/>: 5°).</summary>
        public const float Magnification = 10f;
        public static readonly float FovDegrees = DeviceOptics.FovDegrees(Magnification);

        /// <summary>The beam's band: the designator's 1 064 nm (<see cref="LaserBands.Designator"/> — night vision sees it faint, D-159).</summary>
        public const byte Band = LaserBands.Designator;

        [StructLayout(LayoutKind.Sequential)]
        public struct DesignatorParams
        {
            /// <summary>The type's duty cycle (s) and default code (<see cref="Fdp.Toolkit.Tkb.Domain.LaserDesignatorDto"/>).</summary>
            public float LaseSeconds, CoolSeconds;
            /// <inheritdoc cref="LaseSeconds"/>
            public ushort DefaultCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DesignatorWork
        {
            /// <summary>⛔ Reserved, always 0 — a device counts NOTHING per frame (S1e-3).</summary>
            public uint Ticks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DesignatorBlackboard
        {
            public DesignatorParams Params;
            public DesignatorWork Work;
        }

        /// <summary>The type's data: its <c>Gen.LaserDesignator</c> descriptor when the device's TKB type carries one, else the defaults.</summary>
        [BehaviorResolver(BehaviorNames.DeviceDesignator)]
        public static void Resolve(in NoParams authored, ref DesignatorBlackboard block, EntityRepository world, Entity self)
        {
            var p = new DesignatorParams { LaseSeconds = DefaultLaseSeconds, CoolSeconds = DefaultCoolSeconds, DefaultCode = DefaultCode };
            if (world.IsComponentTypeRegistered<DeviceInfo>() && world.HasComponent<DeviceInfo>(self) && world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()
                && world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db && db.TryGetByType(world.GetComponentRO<DeviceInfo>(self).DeviceType, out var t)
                && t?.GetDescriptor<Fdp.Toolkit.Tkb.Domain.LaserDesignatorDto>() is { } dto)
            {
                p.LaseSeconds = dto.LaseSeconds; p.CoolSeconds = dto.CoolSeconds;
                p.DefaultCode = DesignatorCodes.IsValid(dto.DefaultCode) ? dto.DefaultCode : DesignatorCodes.First;
            }
            block = new DesignatorBlackboard { Params = p };
        }

        // ⛔ Offered to nothing but a device: Requires DeviceInfo (BehaviorFit's AND), so no unit's task picker lists it.
        [Behavior(BehaviorNames.DeviceDesignator, Params = typeof(NoParams), Requires = new[] { typeof(DeviceInfo) },
                  Description = "A device's logic: the laser target designator (run by the device tick, never assigned).")]
        [BTreeDefinition(BehaviorNames.DeviceDesignator, Curated = true, Group = "Library/Devices", ParamsType = typeof(DesignatorBlackboard))]
        public static BTreeBuilder<DesignatorBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DesignatorBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.Work, Tick);

        /// <summary>One tick of the designator (see the class summary). Never ends: Running.</summary>
        [SharedAiAction]
        public static NodeStatus Tick(ref DesignatorParams p, ref DesignatorWork ws, Entity self, EntityRepository world)
        {
            ref var st = ref UnitMemory.Ref<DesignatorState>(world, self);
            if (st.Ready == 0)
            {
                st.Code = DesignatorCodes.IsValid(p.DefaultCode) ? p.DefaultCode : DesignatorCodes.First;
                st.LaseSeconds = p.LaseSeconds > 0f ? p.LaseSeconds : DefaultLaseSeconds;
                st.CoolSeconds = p.CoolSeconds > 0f ? p.CoolSeconds : DefaultCoolSeconds;
                st.Ready = 1;
            }
            Span<int> moved = stackalloc int[DesignatorChannels.Count];
            DevicePressReader.Read(world, self, ref st.Presses, moved);   // ① (re-based on a new operator epoch, R7-11)
            bool live = Fdp.Toolkit.Replication.Attributes.FormAttributes.TimeAdvances(world);
            double now = Fdp.Toolkit.Combat.HitModel.Now(world);

            // ② POWER
            if ((moved[DesignatorChannels.Power] & 1) != 0)
            {
                st.Phase = st.Phase == DesignatorPhase.Off ? DesignatorPhase.Standby : DesignatorPhase.Off;
                st.Queued = 0; st.Ranging = 0;
            }

            // ③ the duty cycle (sim time: it stands in a pause)
            if (st.Phase == DesignatorPhase.Lasing && now - st.LaseFrom >= st.LaseSeconds)
            {
                st.Phase = DesignatorPhase.Cooling;
                st.CoolFrom = st.LaseFrom + st.LaseSeconds;   // exactly at the limit, whatever the frame
            }
            if (st.Phase == DesignatorPhase.Cooling && now - st.CoolFrom >= st.CoolSeconds) st.Phase = DesignatorPhase.Standby;

            // ④ its own spot's answer to the CURRENT request (a spot left from an earlier emission carries an older seq)
            st.Designating = 0;
            if ((st.Ranging != 0 || st.Phase == DesignatorPhase.Lasing) && world.IsComponentTypeRegistered<LaserSpot>() && world.HasComponent<LaserSpot>(self))
            {
                ref readonly var spot = ref world.GetComponentRO<LaserSpot>(self);
                if (spot.PointingSeq == st.Seq)
                {
                    st.HasRange = spot.Count > 0 ? (byte)1 : (byte)0;
                    st.LastRange = spot.Count > 0 ? spot.Points[0].Range : 0f;
                    st.LastHit = spot.Count > 0 ? spot.Points[0].Hit : Entity.Null;
                    st.LastPoint = spot.Count > 0 ? spot.Points[0].Point : default;
                    st.RangePreview = spot.Preview;
                    st.Ranging = 0;   // a RANGE pulse is one measurement
                    st.Designating = st.Phase == DesignatorPhase.Lasing && live && spot.Preview == 0 && spot.Count > 0 ? (byte)1 : (byte)0;
                }
            }

            // ⑤ CODE — STANDBY only
            if (st.Phase == DesignatorPhase.Standby)
                for (int i = 0; i < moved[DesignatorChannels.Code]; i++) st.Code = DesignatorCodes.Next(st.Code);

            // ⑥ LASE — each press in order; a pause QUEUES a lase (R-4300 / D-115), the first live frame starts it
            for (int i = 0; i < moved[DesignatorChannels.Lase]; i++)
            {
                if (st.Phase == DesignatorPhase.Lasing) { st.Phase = DesignatorPhase.Standby; st.Designating = 0; }
                else if (st.Phase == DesignatorPhase.Standby)
                {
                    if (live) StartLase(ref st, now);
                    else st.Queued = st.Queued == 0 ? (byte)1 : (byte)0;   // pressed again in the pause: un-queued
                }
            }
            if (st.Phase != DesignatorPhase.Standby) st.Queued = 0;
            else if (st.Queued != 0 && live) { st.Queued = 0; StartLase(ref st, now); }

            // ⑦ RANGE — one uncoded pulse (powered, not lasing: a lase ranges continuously)
            if (moved[DesignatorChannels.Range] > 0 && st.Phase is DesignatorPhase.Standby or DesignatorPhase.Cooling) { st.Seq++; st.Ranging = 1; }
            if (st.Phase == DesignatorPhase.Off) st.Ranging = 0;

            // ⑧ a TARGET from a linked device (filed into its inbox by the device tick's ingest) — taken even while off, kept only while powered
            while (DeviceMessages.TryTake(world, self, Contracts.TargetLocation.Kind, ref st.Messages, out Contracts.TargetLocation target, out var from))
                if (st.Phase != DesignatorPhase.Off) { st.Target = target; st.TargetFrom = from; st.HasTarget = 1; }

            // ⑨ its own outputs, on change
            DeviceLooks.Follow(world, self, FovDegrees);
            WritePointing(world, self, in st);
            return NodeStatus.Running;
        }

        private static void StartLase(ref DesignatorState st, double now)
        {
            st.Phase = DesignatorPhase.Lasing;
            st.LaseFrom = now;
            st.Seq++;
            st.Ranging = 0;
        }

        private static void WritePointing(EntityRepository world, Entity self, in DesignatorState st)
        {
            if (!world.IsComponentTypeRegistered<LaserPointing>()) return;
            bool lasing = st.Phase == DesignatorPhase.Lasing;
            var next = new LaserPointing
            {
                On = lasing || st.Ranging != 0 ? (byte)1 : (byte)0, Band = Band, Power = 1f, Direction = default, Seq = st.Seq,
                Code = lasing ? st.Code : (ushort)0,   // D-114 — the code rides the lase; a RANGE pulse is no designation
            };
            if (world.HasComponent<LaserPointing>(self))
            {
                ref readonly var cur = ref world.GetComponentRO<LaserPointing>(self);
                if (cur.On == next.On && cur.Band == next.Band && cur.Power == next.Power && cur.Direction == next.Direction && cur.Seq == next.Seq && cur.Code == next.Code) return;
                world.SetComponent(self, next);
            }
            else world.AddComponent(self, next);   // added WITH its value (the parallel rule)
        }
    }
}
