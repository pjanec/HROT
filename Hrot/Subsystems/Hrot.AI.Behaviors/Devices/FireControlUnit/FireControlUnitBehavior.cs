using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Hrot.AI.Behaviors.Brains;   // the StatefulAction builder extension
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Devices.FireControlUnit
{
    /// <summary>
    /// Simulated devices SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13b D-92 - D-96 / D-112, sec 3.13g D-172) - THE FIRE-CONTROL UNIT's logic: ONE stateful action on the
    /// DEVICE entity, run every frame (paused or not) by the device tick. Its state is <see cref="FireControlUnitState"/> (unit memory). It is the ONE unit behaviour (D-172): the
    /// launcher sight unit is its MOUNTED, one-weapon profile (this slice); the control unit (S4c) and the lock tracker (S4d) add profiles and aim methods, never a second behaviour.
    /// <para>Each tick: (1) the presses (<see cref="DevicePressReader"/>); (2) POWER acts at once - the device's own state, in pause too: it starts the self-test (<see cref="TestSeconds"/>
    /// of SIM time, frozen in pause) or switches off; (3) the weapon it is mounted in (its host station) - the rounds it holds and whether it is deployed; (4) the procedure
    /// <c>STANDBY ->(ARM) ARMED ->(FIRE) GUIDING ->(the flight ends) ARMED / STANDBY</c>: ARM needs a round (D-112: refused, never queued - in pause too), FIRE acts on a live frame only
    /// (D-78: refused in pause, never carried to the first live frame), the flight is read from <see cref="GuidedFlight"/> (the muscle writes it on this device, D-88); (5) VIEW
    /// toggles the round's view while it flies (D-178 (1)), and gives the picture back by itself when the flight ends ((2)); (6) on a live frame its <see cref="FireControl"/> - the guide line
    /// (its world pose, the look the station sends, D-96), the launch seq, <see cref="FireControl.Ready"/> - which the weapon's controller reads (D-94); (7) its <see cref="DeviceView"/>.</para>
    /// <para>The parallel rule: it writes ONLY its own entity (memory, <see cref="FireControl"/>, <see cref="DeviceView"/>, <see cref="DevicePose"/>); it reads its weapon's
    /// <see cref="WeaponState"/> / <see cref="WeaponSystemState"/> (declared tick-start reads). 0 B per steady tick. A device counts NOTHING per frame (S1e-3): its self-test
    /// counts SIM time.</para>
    /// </summary>
    public static class FireControlUnitBehavior
    {
        /// <summary>The power-up self-test (sim s) - NOTE: mirrors the TKB type 644's <c>DeviceDefDto.TestSeconds</c> (pinned by <c>FireControlUnitTests</c>).</summary>
        public const float TestSeconds = 3f;

        /// <summary>The sight's fixed magnification and the field of view a view bound to it shows - NOTE: mirrors the type's <c>Magnifications</c> (pinned).</summary>
        public const float Magnification = 10f;
        public static readonly float FovDegrees = DeviceOptics.FovDegrees(Magnification);

        /// <summary>A FIRE the weapon never took (no controller, a refusal) is given up after this long (sim s) and the unit returns to ARMED / STANDBY, never GUIDING for ever.</summary>
        public const double LaunchGraceSeconds = 10.0;

        [StructLayout(LayoutKind.Sequential)]
        public struct FireControlUnitParams { public byte Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct FireControlUnitWork
        {
            /// <summary>NOTE: Reserved, always 0 - a device counts nothing per frame (S1e-3).</summary>
            public uint Ticks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FireControlUnitBlackboard
        {
            public FireControlUnitParams Params;
            public FireControlUnitWork Work;
        }

        [BehaviorResolver(BehaviorNames.DeviceFireControlUnit)]
        public static void Resolve(in NoParams authored, ref FireControlUnitBlackboard block, EntityRepository world, Entity self) => block = default;

        // NOTE: Offered to nothing but a device: Requires DeviceInfo (BehaviorFit's AND), so no unit's task picker lists it.
        [Behavior(BehaviorNames.DeviceFireControlUnit, Params = typeof(NoParams), Requires = new[] { typeof(DeviceInfo) },
                  Description = "A device's logic: the fire-control unit (run by the device tick, never assigned).")]
        [BTreeDefinition(BehaviorNames.DeviceFireControlUnit, Curated = true, Group = "Library/Devices", ParamsType = typeof(FireControlUnitBlackboard))]
        public static BTreeBuilder<FireControlUnitBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<FireControlUnitBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.Work, Tick);

        /// <summary>One tick of the fire-control unit (see the class summary). Never ends: Running.</summary>
        [SharedAiAction]
        public static NodeStatus Tick(ref FireControlUnitParams p, ref FireControlUnitWork ws, Entity self, EntityRepository world)
        {
            ref var st = ref UnitMemory.Ref<FireControlUnitState>(world, self);
            Span<int> moved = stackalloc int[FireControlUnitChannels.Count];
            DevicePressReader.Read(world, self, ref st.Presses, moved);   // (1) (re-based on a new operator epoch, R7-11)
            bool live = Fdp.Toolkit.Replication.Attributes.FormAttributes.TimeAdvances(world);
            double now = HitModel.Now(world);
            float dt = st.LastNow > 0d && now > st.LastNow ? (float)(now - st.LastNow) : 0f;
            st.LastNow = now;

            // (2) POWER: the device's own state, in pause too
            if ((moved[FireControlUnitChannels.Power] & 1) != 0)
            {
                if (st.Phase == FireControlPhase.Off) { st.Phase = TestSeconds > 0f ? FireControlPhase.Test : FireControlPhase.Standby; st.TestLeft = TestSeconds; }
                else { st.Phase = FireControlPhase.Off; st.ViewFromRound = 0; st.Flying = 0; }   // a round in flight flies on unguided: no guide line (D-93)
            }

            // (3) the weapon it is mounted in, and the flight it launched
            var weapon = WeaponOf(world, self);
            st.Weapon = weapon;
            st.Missiles = RoundsOf(world, weapon);
            bool loaded = st.Missiles > 0;
            var flight = FlightOf(world, self, out bool hasFlight);
            bool flightLive = hasFlight && flight.Live != 0 && flight.LaunchSeq == st.LaunchSeq && st.LaunchSeq != 0;

            // (4) the procedure
            if (st.Phase == FireControlPhase.Test)
            {
                st.TestLeft = MathF.Max(0f, st.TestLeft - dt);   // SIM time: a pause stands still (dt is 0)
                if (st.TestLeft <= 0f) st.Phase = FireControlPhase.Standby;
            }
            if (st.Phase == FireControlPhase.Armed && !loaded) st.Phase = FireControlPhase.Standby;   // a round taken out while armed (D-112)
            if (st.Phase == FireControlPhase.Standby && moved[FireControlUnitChannels.Arm] > 0 && loaded) st.Phase = FireControlPhase.Armed;   // ARM needs a round: refused, never queued
            bool launched = false;
            bool ready = st.Phase == FireControlPhase.Armed && loaded && live && Deployed(world, weapon) && !Reloading(world, weapon);
            if (st.Phase == FireControlPhase.Armed && moved[FireControlUnitChannels.Fire] > 0 && ready)
            {
                unchecked { st.LaunchSeq++; }
                st.LaunchedAt = now;
                st.Flying = 0;
                st.Phase = FireControlPhase.Guiding;
                flightLive = false;
                launched = true;
            }
            else if (st.Phase == FireControlPhase.Guiding)
            {
                if (flightLive) st.Flying = 1;
                else if (st.Flying != 0 || now - st.LaunchedAt > LaunchGraceSeconds)   // the flight ended - or the weapon never took the shot
                {
                    st.Phase = loaded ? FireControlPhase.Armed : FireControlPhase.Standby;
                    st.Flying = 0;
                }
            }
            if (st.Phase != FireControlPhase.Armed) ready = false;

            // (5) VIEW: the round's picture, only while it flies; it ends with the flight (D-178)
            if ((moved[FireControlUnitChannels.View] & 1) != 0 && flightLive && st.Phase != FireControlPhase.Off) st.ViewFromRound = (byte)(st.ViewFromRound ^ 1);
            if (!flightLive || st.Phase == FireControlPhase.Off) st.ViewFromRound = 0;
            st.Ready = ready ? (byte)1 : (byte)0;

            // (6) what it asks of its weapon: the guide line on live frames only (a pause freezes the command, D-83); none while off
            var fc = FireControlOf(world, self);
            // the launch tick still publishes READY: the controller fires when it sees the seq move AND Ready (D-94) - the unit is GUIDING from this tick, READY only a lamp from the next
            fc.Ready = launched ? (byte)1 : st.Ready;
            fc.ViewFromRound = st.ViewFromRound;
            if (live || st.Phase == FireControlPhase.Off)
            {
                fc.LaunchSeq = st.LaunchSeq;
                if (st.Phase == FireControlPhase.Off) { fc.GuideOrigin = default; fc.GuideDir = default; }
                else if (DevicePoses.TryWorld(world, self, out var origin, out var rotation))
                {
                    fc.GuideOrigin = origin;
                    fc.GuideDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rotation));
                }
            }
            FireControls.Write(world, self, in fc);

            // (7) its view: the round's chase picture while asked and flying, else the operator's own (the optic)
            var round = flightLive ? flight.Round : Entity.Null;
            DeviceViews.Write(world, self, st.ViewFromRound != 0 && !round.IsNull
                ? new DeviceView { Entity = round, Mode = DeviceViewMode.Chase }
                : default);
            DeviceLooks.Follow(world, self, FovDegrees);   // where its operator looks, at its optic's field
            return NodeStatus.Running;
        }

        /// <summary>The weapon the unit is mounted in: the host of its station (<see cref="IsEmbarkedTag.VehicleEntity"/>), or null.</summary>
        public static Entity WeaponOf(EntityRepository world, Entity self)
        {
            if (!world.IsComponentTypeRegistered<IsEmbarkedTag>() || !world.HasComponent<IsEmbarkedTag>(self)) return Entity.Null;
            var host = world.GetComponentRO<IsEmbarkedTag>(self).VehicleEntity;
            return !host.IsNull && world.IsAlive(host) ? host : Entity.Null;
        }

        /// <summary>The rounds the weapon holds (its mount 0's total), 0 when it has none or there is no weapon.</summary>
        public static int RoundsOf(EntityRepository world, Entity weapon)
            => !weapon.IsNull && world.HasComponent<WeaponState>(weapon) ? Math.Max(0, world.GetComponentRO<WeaponState>(weapon).Ammo) : 0;

        /// <summary>The weapon is deployed (a weapon with no deploy state counts as deployed - a placed weapon stands ready).</summary>
        public static bool Deployed(EntityRepository world, Entity weapon)
            => weapon.IsNull || !world.IsComponentTypeRegistered<WeaponSystemState>() || !world.HasComponent<WeaponSystemState>(weapon)
               || world.GetComponentRO<WeaponSystemState>(weapon).Deploy == WeaponDeploy.Deployed;

        /// <summary>A reload runs: the round cannot leave yet (the READY lamp stays off - a FIRE would be dropped by the weapon).</summary>
        public static bool Reloading(EntityRepository world, Entity weapon)
            => !weapon.IsNull && world.HasComponent<WeaponState>(weapon) && Magazine.Reloading(world.GetComponentRO<WeaponState>(weapon));

        private static GuidedFlight FlightOf(EntityRepository world, Entity self, out bool has)
        {
            has = world.IsComponentTypeRegistered<GuidedFlight>() && world.HasComponent<GuidedFlight>(self);
            return has ? world.GetComponentRO<GuidedFlight>(self) : default;
        }

        private static FireControl FireControlOf(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<FireControl>() && world.HasComponent<FireControl>(self) ? world.GetComponentRO<FireControl>(self) : default;
    }
}
