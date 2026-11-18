using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (VA-S2, VA-4; 📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4, §5) — THE writer of <see cref="VehicleOpenings"/>,
    /// on the BOARDING ARBITER (the passenger list's owner, <see cref="Embarkation.OwnsVehicle"/>). It WATCHES the passenger list rather than every boarding path:
    /// a seat whose occupant changed (a man boarded or left — embark, dismount, ejection, a re-carry) opens every opening of its type that serves that seat's ROLE
    /// (Opening since now, once); an opening nobody has used for its open seconds + <see cref="HoldSeconds"/> starts Closing. The animation is derived
    /// (<see cref="VehicleOpenings.Progress"/>), so a ramp writes twice per dismount. First sight (a scenario, a reload, a late join, a claim that moved here)
    /// opens nothing. ⏳ A crewman's "head out" (observing, an order) — a request to this owner, not built (VA-S2 follow-up).
    /// ⭐ Parallel contract: serial, in the <c>Interactions</c> capability (after the appliers); writes only vehicles whose passenger list this node owns; the
    /// component is ADDED with its final value (D-31). 0 B per steady frame (a track is allocated on first sight).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class VehicleOpeningsSystem : IEcsModuleSystem
    {
        /// <summary>How long an opening stays open after its last use before it closes (s).</summary>
        public const double HoldSeconds = 2.0;

        private sealed class Track
        {
            public readonly Entity[] Occupant = new Entity[PassengerBuffer.Capacity];
            public readonly double[] LastUse = new double[VehicleOpenings.Capacity];
        }

        private readonly Dictionary<Entity, Track> _tracks = new();
        private EntityRepository? _for;
        private int _epoch;
        private EntityQuery? _vehicles;

        /// <summary>Openings this node opened (rails, diagnostics).</summary>
        public long Opened { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<PassengerBuffer>() || !repo.IsComponentTypeRegistered<VehicleOpenings>()
                || !repo.IsComponentTypeRegistered<TkbIdentity>()) return;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) { _tracks.Clear(); return; }   // the recording holds what the arbiter wrote
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(repo, ref _epoch) | !ReferenceEquals(repo, _for) || _vehicles == null)
            {
                _for = repo;
                _tracks.Clear();
                _vehicles = repo.Query().With<PassengerBuffer>().With<TkbIdentity>().Build();
            }
            if (!repo.HasSingletonUnmanaged<GlobalTime>() || !repo.HasSingletonManaged<ITkbDatabase>() || repo.GetSingletonManaged<ITkbDatabase>() is not { } tkb) return;
            double now = repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
            bool seatRoles = repo.IsComponentTypeRegistered<CarrierSeats>();

            foreach (var v in _vehicles!)
            {
                if (!Embarkation.OwnsVehicle(repo, v) || !tkb.TryGetByType(repo.GetComponentRO<TkbIdentity>(v).TkbType, out var type)) continue;
                var openings = VehicleOpeningsCatalog.Of(type);
                if (openings.Length == 0) continue;
                ref readonly var list = ref repo.GetComponentRO<PassengerBuffer>(v);

                if (!_tracks.TryGetValue(v, out var track))
                {
                    // ⭐ first sight: remember who sits where; a vehicle without the component gets it, every opening closed (added with its final value, D-31)
                    track = new Track();
                    for (int s = 0; s < PassengerBuffer.Capacity; s++) track.Occupant[s] = list.Passengers[s];
                    _tracks[v] = track;
                    if (!repo.HasComponent<VehicleOpenings>(v)) repo.AddComponent(v, new VehicleOpenings { Count = (byte)openings.Length });
                    continue;
                }

                // ① a seat changed hands ⇒ the openings serving its role are used now
                int used = 0;
                for (int s = 0; s < PassengerBuffer.Capacity; s++)
                {
                    var who = list.Passengers[s];
                    if (who == track.Occupant[s]) continue;
                    track.Occupant[s] = who;
                    var role = seatRoles && repo.HasComponent<CarrierSeats>(v) ? repo.GetComponentRO<CarrierSeats>(v).Roles[s] : SeatRole.Passenger;
                    used |= VehicleOpeningsCatalog.ServingMask(openings, role);
                }
                for (int i = 0; i < openings.Length; i++) if ((used & (1 << i)) != 0) track.LastUse[i] = now;

                // ② open what is used; close what has been idle past its open time + the hold
                if (!repo.HasComponent<VehicleOpenings>(v)) continue;
                ref readonly var cur = ref repo.GetComponentRO<VehicleOpenings>(v);
                int toOpen = 0, toClose = 0;
                for (int i = 0; i < openings.Length && i < cur.Count; i++)
                {
                    bool open = cur.IsOpenOrOpening(i);
                    if ((used & (1 << i)) != 0) { if (!open) toOpen |= 1 << i; }
                    else if (open && now - track.LastUse[i] >= openings[i].OpenSeconds + HoldSeconds) toClose |= 1 << i;
                }
                if ((toOpen | toClose) == 0) continue;
                ref var rw = ref repo.GetComponentRW<VehicleOpenings>(v);
                for (int i = 0; i < openings.Length && i < rw.Count; i++)
                {
                    if ((toOpen & (1 << i)) != 0) { rw.Slots[i] = new OpeningSlot { State = OpeningState.Opening, Since = now }; Opened++; }
                    else if ((toClose & (1 << i)) != 0) rw.Slots[i] = new OpeningSlot { State = OpeningState.Closing, Since = now };
                }
            }
        }
    }
}
