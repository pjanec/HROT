using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2a (R-224) — the applier the embark commands were designed for (<c>edit-1</c> Phase 3: "consumed by
    /// execution systems in the kernel"): it reads <see cref="EmbarkEntityCommand"/> / <see cref="DisembarkEntityCommand"/> /
    /// <see cref="EjectPassengersCommand"/> — raised here, or carried here from another node by the interaction transport — and applies
    /// each through <see cref="Embarkation"/> ONLY on the vehicle's owner (<see cref="Embarkation.OwnsVehicle"/>). The answer is the
    /// vehicle's own state; there is no reply. ⛔ Replaces <c>EditorCargoSystem</c>, which was a second applier for the same commands.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7, §7.1a.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
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
                var boards = repo.Bus.Read<EmbarkEntityCommand>();
                for (int i = 0; i < boards.Length; i++)
                {
                    ref readonly var c = ref boards[i];
                    if (!Embarkation.OwnsVehicle(repo, c.Vehicle)) continue;
                    Count(Embarkation.TryEmbark(repo, c.Passenger, c.Vehicle, c.Role, c.Mode) is EmbarkResult.Applied or EmbarkResult.AlreadyAboard);
                }
            }
            if (repo.Bus.IsRegistered<DisembarkEntityCommand>())
            {
                var leaves = repo.Bus.Read<DisembarkEntityCommand>();
                for (int i = 0; i < leaves.Length; i++)
                {
                    ref readonly var c = ref leaves[i];
                    var vehicle = VehicleOf(repo, c.Passenger, c.Vehicle);
                    if (!Embarkation.OwnsVehicle(repo, vehicle)) continue;
                    Count(Embarkation.Disembark(repo, c.Passenger, vehicle));
                }
            }
            if (repo.Bus.IsRegistered<EjectPassengersCommand>())
            {
                var ejects = repo.Bus.Read<EjectPassengersCommand>();
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
