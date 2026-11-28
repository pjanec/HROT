using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.5, D-72) — THE DEVICE TICK: every device's logic, every
    /// frame, PAUSED OR NOT. A device is logic that REUSES the brain machinery, never an AI: so it is the brain tick's OWN routine
    /// (<see cref="BrainTickSystem"/>, serial and parallel paths, one code), filtered to <see cref="DeviceInfo"/> and scheduled
    /// <see cref="TickPolicy.EveryFrame"/> — while <see cref="BrainTickSystem"/> walks every brain EXCEPT the devices.
    /// <para>📐 Why a separate EVERY-FRAME entry (not the brain tick's <c>WhenTimeAdvances</c>): an every-frame system reads each frame's events once
    /// (no step window, <c>SystemScheduler.cs:240-270</c>), so nothing a device saw in a pause is read again at resume; and no AI cadence policy can
    /// reach a device. ⭐ As built, a SUBCLASS rather than the design's extracted runner: it is literally the same tick code, with each instance's own
    /// records (the finish latch, the relayout record) — less code, no second loop (ruling 9).</para>
    /// <para>The pre-pass (serial, before the phase): ⛔ nothing while a replay plays (a paused replay must never overwrite the restored state — R3-3);
    /// ⭐ a device whose behaviour is not running (new, finished, faulted, cleared) is STARTED from its <see cref="DeviceInfo.Behavior"/> through the
    /// configured ingress instance (R6-2, R7-12) — in pause too, so a trainee can use a device before time ever advances; a device's logic never stops.
    /// Pause is observation only: the device ticks on SIM time (dt = 0 in pause — R6-4).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class DeviceTickSystem : BrainTickSystem
    {
        private readonly BehaviorIngressSystem _ingress;
        private readonly bool _gate;
        private EntityQuery? _devices;
        private EntityRepository? _queryFor;
        // a device whose start was REFUSED (an unknown behaviour, the gate) is not retried every frame: no string, no allocation — until its run changes
        private readonly System.Collections.Generic.Dictionary<int, uint> _refusedAtInstance = new();

        /// <summary>⭐ SD-S5v (D-138) — the device-side merge of every source's sparse input events (its own wall clock and bookkeeping; a rail sets the clock).</summary>
        public DeviceInputMerge Merge { get; } = new();

        /// <summary>Starts ever made (a rail reads it; never reset).</summary>
        public int Starts { get; private set; }

        public DeviceTickSystem(BehaviorRegistry registry, BehaviorIngressSystem ingress, bool gateOnAuthority = false)
            : base(registry, gateOnAuthority)
        {
            _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
            _gate = gateOnAuthority;
        }

        protected override bool IsDeviceTick => true;

        protected override bool BeforeDeviceTick(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<DeviceInfo>() || !repo.IsComponentTypeRegistered<BehaviorState>()) return false;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) return false;
            if (_devices == null || !ReferenceEquals(repo, _queryFor))
            {
                _devices = repo.Query().With<DeviceInfo>().WithOwnedWhen<BehaviorState>(_gate).Build();
                _queryFor = repo;
                // ⭐ S1d-3 — the device input's event is the device tick's to read: registered here (as PossessionSystem registers the body's), so a
                //   station's input reaches a device on every host — ⛔ before, only the rails registered it and no production frame was ever ingested
                if (!repo.Bus.IsRegistered<DeviceInputEvent>()) repo.Bus.Register<DeviceInputEvent>();
                if (!repo.IsComponentTypeRegistered<DeviceInput>()) repo.RegisterComponent<DeviceInput>();
                // ⭐ SD-S5b (D-119) — and the device MESSAGE: the receiver's ingest lives here, so the event is registered here (a send from the device phase rides
                //   the batch's command buffer, which needs the stream)
                if (!repo.Bus.IsRegistered<DeviceMessageEvent>()) repo.Bus.Register<DeviceMessageEvent>();
                if (!repo.IsComponentTypeRegistered<DeviceInbox>()) repo.RegisterComponent<DeviceInbox>();
            }
            foreach (var e in _devices)
            {
                DeviceOperators.Sync(repo, e);   // ⭐ S1b-1 — who operates it, every frame (R7-1); writes only on a change
                ref readonly var b = ref repo.GetComponentRO<BehaviorState>(e);
                if (b.ActiveBehaviorHash != BehaviorIds.None) continue;
                ref readonly var info = ref repo.GetComponentRO<DeviceInfo>(e);
                if (info.Behavior.IsEmpty) continue;
                if (_refusedAtInstance.TryGetValue(e.Index, out uint at) && at == b.InstanceId) continue;
                string name = info.Behavior.ToString();   // a start is rare (a new device, a finish): the string is fine here
                if (_ingress.AssignNow(repo, e, name, "{}", BehaviorOrigin.Self)) { Starts++; _refusedAtInstance.Remove(e.Index); }
                else _refusedAtInstance[e.Index] = b.InstanceId;
            }
            // ⭐ S1b-2 / ⭐⭐ SD-S5v (D-138) — the DEVICE INGEST: this frame's SPARSE input events aimed at a device this node runs, sent by the station its
            //   row names (the existing HeldBy gate, PossessionMode.Operated), merged IN ARRIVAL ORDER into the device's own DeviceInput (DeviceInputMerge:
            //   presses add up, fields last-writer-wins under the source priority). Every frame, so a press in pause reaches the device, each event read once.
            if (repo.Bus.IsRegistered<DeviceInputEvent>() && repo.IsComponentTypeRegistered<DeviceInput>() && repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>())
                foreach (ref readonly var f in repo.ReadEvents<DeviceInputEvent>())
                {
                    if (!DeviceMarker.IsDevice(repo, f.Device) || !Fdp.Toolkit.Behavior.Possession.PossessionSystem.OwnsBrain(repo, f.Device)) continue;
                    if (!Fdp.Toolkit.Behavior.Possession.PossessionSystem.HeldBy(repo, f.Device, f.Station)) continue;
                    if (repo.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(f.Device).Mode != Fdp.Toolkit.Behavior.Possession.PossessionMode.Operated) continue;
                    bool has = repo.HasComponent<DeviceInput>(f.Device);
                    if (!has) Merge.Forget(f.Device);   // a clean input (a new operator, R7-11): every source starts afresh
                    var input = has ? repo.GetComponentRO<DeviceInput>(f.Device) : default;
                    if (!Merge.Apply(in f, ref input)) continue;
                    if (has) repo.SetComponent(f.Device, input); else repo.AddComponent(f.Device, input);
                }
            // ⭐⭐ SD-S5b (D-119) — the MESSAGE ingest, beside the input merge: last frame's DeviceMessageEvents filed into each target's inbox (newest two), which its
            //   own behaviour reads this frame — one tick late, applied by the receiver only. No operator gate: the address is the authorisation (D-53).
            DeviceMessages.Ingest(repo);
            return true;
        }

        protected override void RequestRestart(EntityRepository repo, Entity entity, string behaviorName, string json)
        {
            if (_ingress.AssignNow(repo, entity, behaviorName, json, BehaviorOrigin.Self)) Starts++;
        }
    }
}
