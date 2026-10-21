using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2a (R-224) — the applier the embark commands were designed for (<c>edit-1</c> Phase 3: "consumed by
    /// execution systems in the kernel"): it reads <see cref="EmbarkEntityCommand"/> / <see cref="DisembarkEntityCommand"/> /
    /// <see cref="EjectPassengersCommand"/> / <see cref="StartDescentCommand"/> (airborne insertion B-4, applied on the PASSENGER's position
    /// owner) — raised here, or carried here from another node by the interaction transport — and applies
    /// each through <see cref="Embarkation"/> ONLY on the vehicle's owner (<see cref="Embarkation.OwnsVehicle"/>). The answer is the
    /// vehicle's own state; there is no reply. ⛔ Replaces <c>EditorCargoSystem</c>, which was a second applier for the same commands.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7, §7.1a.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class EmbarkationSystem : IEcsModuleSystem
    {
        /// <summary>Commands this node applied (a rail and a diagnostics counter read it).</summary>
        public long Applied { get; private set; }
        /// <summary>Commands this node owned but the rules refused (vehicle full, passenger not aboard, …).</summary>
        public long Refused { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            if (repo.Bus.IsRegistered<EmbarkEntityCommand>())
            {
                var boards = repo.ReadEvents<EmbarkEntityCommand>();
                for (int i = 0; i < boards.Length; i++)
                {
                    ref readonly var c = ref boards[i];
                    if (!Embarkation.OwnsVehicle(repo, c.Vehicle)) continue;
                    Count(Embarkation.TryEmbark(repo, c.Passenger, c.Vehicle, c.Role, c.Mode, c.Assistant) is EmbarkResult.Applied or EmbarkResult.AlreadyAboard);   // ⭐ T-a
                }
            }
            if (repo.Bus.IsRegistered<DisembarkEntityCommand>())
            {
                var leaves = repo.ReadEvents<DisembarkEntityCommand>();
                for (int i = 0; i < leaves.Length; i++)
                {
                    ref readonly var c = ref leaves[i];
                    var vehicle = VehicleOf(repo, c.Passenger, c.Vehicle);
                    if (!Embarkation.OwnsVehicle(repo, vehicle)) continue;
                    Count(Embarkation.Disembark(repo, c.Passenger, vehicle, c.Exit, c.AnchorIndex, c.Target));
                }
            }
            // ⭐ airborne insertion B-4 — a descent decided on the carrier's owner, applied where the man's position is owned
            if (repo.Bus.IsRegistered<StartDescentCommand>())
            {
                var starts = repo.ReadEvents<StartDescentCommand>();
                for (int i = 0; i < starts.Length; i++)
                {
                    ref readonly var c = ref starts[i];
                    if (!c.IsRemote || !repo.IsAlive(c.Passenger) || !Embarkation.OwnsPosition(repo, c.Passenger)) continue;
                    Count(Fdp.Toolkit.Motion.Descents.Start(repo, c.Passenger, c.Anchor, c.Exit, c.AnchorIndex, c.Target));
                }
            }
            if (repo.Bus.IsRegistered<EjectPassengersCommand>())
            {
                var ejects = repo.ReadEvents<EjectPassengersCommand>();
                for (int i = 0; i < ejects.Length; i++)
                {
                    ref readonly var c = ref ejects[i];
                    if (!Embarkation.OwnsVehicle(repo, c.Vehicle)) continue;
                    Embarkation.EjectAll(repo, c.Vehicle);
                    Applied++;
                }
            }
        }

        /// <summary>The vehicle a disembark targets: the command's, else the one the passenger's tag names.</summary>
        public static Entity VehicleOf(EntityRepository repo, Entity passenger, Entity vehicle)
        {
            if (!vehicle.IsNull) return vehicle;
            return repo.IsAlive(passenger) && repo.HasComponent<Components.IsEmbarkedTag>(passenger)
                ? repo.GetComponentRO<Components.IsEmbarkedTag>(passenger).VehicleEntity
                : Entity.Null;
        }

        private void Count(bool applied)
        {
            if (applied) Applied++; else Refused++;
        }
    }
}
