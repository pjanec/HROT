using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (vehicle appearance VA-S3, VA-3; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6b) — the light rule: discipline ×
    /// darkness × engine; road lights only when dark under Normal, the convoy marker under Convoy, nothing blacked out or with the engine off, an aircraft's nav and
    /// anti-collision whenever its engine runs (day too); written by the lights' owner only, on change, 0 B steady.
    /// </summary>
    public sealed class VehicleLightsTests : IDisposable
    {
        private const long Tank = 400, Heli = 401;
        private readonly EntityRepository _w = new();
        private readonly LightRuleSystem _sys = new();
        private double _t;

        public VehicleLightsTests()
        {
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<VehicleLights>();
            _w.RegisterComponent<HeatSource>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterEvent<LightsRequestEvent>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("Tank", Tank) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 1 } });
            db.Register(new TkbTemplate("Heli", Heli) { DisType = new DISEntityType { Kind = 1, Domain = 2, Category = 21 } });
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
        }

        public void Dispose() => _w.Dispose();

        private LightDto[] LightsOf(long type)
        {
            Assert.True(_w.GetSingletonManaged<ITkbDatabase>()!.TryGetByType(type, out var t));
            return VehicleLightsCatalog.Of(t);
        }

        private Entity Put(long type, HeatSourceState engine)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            _w.AddComponent(e, new SimTransform());
            _w.AddComponent(e, HeatSource.Of(engine, (SimInstant)0.0, (SimInstant)0.0));
            return e;
        }

        private void Step(double s = 0.5)
        {
            _t += s;
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = _t });
            _sys.Execute(_w, (float)s);
            _w.Bus.SwapBuffers();   // a frame ends: this frame's requests are read once
        }

        /// <summary>
        /// ⭐ The rule, pure: an MBT's class lights (head 0, tail 1, brake 2, convoy 3) — head + tail only dark under Normal, the convoy marker only under Convoy, nothing
        /// blacked out / off / engine off, brake never stored; a helicopter's nav + anti-collision whenever its engine runs, day or night, never blacked out.
        /// 🔴 Red-proof: drop the darkness term ⇒ the day row lights the tank ⇒ red.
        /// </summary>
        [Fact]
        public void CE4670_TheLightRule_DisciplineTimesDarknessTimesEngine()
        {
            var tank = LightsOf(Tank);
            Assert.Equal(new[] { LightKind.Head, LightKind.Tail, LightKind.Brake, LightKind.Convoy }, Array.ConvertAll(tank, l => l.Kind));
            Assert.Equal(0b0011u, LightRuleSystem.MaskOf(tank, LightDiscipline.Normal, dark: true, engine: true));
            Assert.Equal(0u, LightRuleSystem.MaskOf(tank, LightDiscipline.Normal, dark: false, engine: true));
            Assert.Equal(0b1000u, LightRuleSystem.MaskOf(tank, LightDiscipline.Convoy, dark: true, engine: true));
            Assert.Equal(0u, LightRuleSystem.MaskOf(tank, LightDiscipline.Blackout, dark: true, engine: true));
            Assert.Equal(0u, LightRuleSystem.MaskOf(tank, LightDiscipline.Normal, dark: true, engine: false));

            var heli = LightsOf(Heli);
            Assert.Equal(0b0011u, LightRuleSystem.MaskOf(heli, LightDiscipline.Normal, dark: false, engine: true));   // nav + anti-collision, by day
            Assert.Equal(0u, LightRuleSystem.MaskOf(heli, LightDiscipline.Blackout, dark: true, engine: true));
        }

        /// <summary>
        /// ⭐ The system: a helicopter with its engine running gets its beacon on by day (added with its final value), stamped when it changes (engine off ⇒ dark);
        /// an authored Blackout holds; a helicopter whose lights another node owns is left alone; the steady frame allocates nothing.
        /// </summary>
        [Fact]
        public void CE4670_TheOwnerSwitchesThem_OnChangeOnly_AndAnAuthoredBlackoutHolds()
        {
            var heli = Put(Heli, HeatSourceState.Running);
            var dark = Put(Heli, HeatSourceState.Running);
            _w.AddComponent(dark, new VehicleLights { Discipline = LightDiscipline.Blackout });
            var remote = Put(Heli, HeatSourceState.Running);
            _w.AddComponent(remote, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            Step();
            Assert.Equal(0b0011u, _w.GetComponentRO<VehicleLights>(heli).OnMask);
            Assert.Equal(0u, _w.GetComponentRO<VehicleLights>(dark).OnMask);
            Assert.False(_w.HasComponent<VehicleLights>(remote));

            Step(); Step();
            Assert.Equal(0.5, _w.GetComponentRO<VehicleLights>(heli).Since);   // unchanged ⇒ not re-stamped
            _w.SetComponent(heli, HeatSource.Of(HeatSourceState.Off, (SimInstant)_t, (SimInstant)0.0));
            Step();
            Assert.Equal(0u, _w.GetComponentRO<VehicleLights>(heli).OnMask);
            Assert.Equal(_t, _w.GetComponentRO<VehicleLights>(heli).Since);

            for (int i = 0; i < 5; i++) Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Step();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>
        /// ⭐ VA-3 — a station's light switch (a request to the lights owner): the discipline is set and the mask follows the rule AT ONCE (not at the next
        /// half-second step); a helicopter running by day blacked out goes dark, back to Normal its nav + anti-collision come on; one never seen gets the component
        /// with its final value; a vehicle whose lights another node owns is left to it.
        /// 🔴 Red-proof: apply the switch without re-evaluating the mask ⇒ the blacked-out helicopter keeps its lights until the next step ⇒ red.
        /// </summary>
        [Fact]
        public void CE4670_AStationsSwitch_SetsTheDiscipline_AndTheMaskFollowsAtOnce()
        {
            var heli = Put(Heli, HeatSourceState.Running);
            Step();
            Assert.Equal(0b0011u, _w.GetComponentRO<VehicleLights>(heli).OnMask);   // nav + anti-collision by day

            Assert.True(VehicleRequests.SwitchLights(_w, heli, LightDiscipline.Blackout));
            _w.Bus.SwapBuffers();
            _sys.Execute(_w, 0f);                                                    // same instant: no rule step
            _w.Bus.SwapBuffers();
            Assert.Equal(LightDiscipline.Blackout, _w.GetComponentRO<VehicleLights>(heli).Discipline);
            Assert.Equal(0u, _w.GetComponentRO<VehicleLights>(heli).OnMask);
            Assert.Equal(_t, _w.GetComponentRO<VehicleLights>(heli).Since);
            for (int i = 0; i < 4; i++) Step();
            Assert.Equal(0u, _w.GetComponentRO<VehicleLights>(heli).OnMask);         // the rule keeps it

            VehicleRequests.SwitchLights(_w, heli, LightDiscipline.Normal);
            var fresh = Put(Heli, HeatSourceState.Running);
            VehicleRequests.SwitchLights(_w, fresh, LightDiscipline.Off);
            var theirs = Put(Heli, HeatSourceState.Running);
            _w.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            VehicleRequests.SwitchLights(_w, theirs, LightDiscipline.Blackout);
            _w.Bus.SwapBuffers();
            _sys.Execute(_w, 0f);
            _w.Bus.SwapBuffers();
            Assert.Equal(0b0011u, _w.GetComponentRO<VehicleLights>(heli).OnMask);
            Assert.Equal(LightDiscipline.Off, _w.GetComponentRO<VehicleLights>(fresh).Discipline);
            Assert.Equal(0u, _w.GetComponentRO<VehicleLights>(fresh).OnMask);
            Assert.False(_w.HasComponent<VehicleLights>(theirs));
            Assert.Equal(3L, _sys.Requests);
        }
    }
}
