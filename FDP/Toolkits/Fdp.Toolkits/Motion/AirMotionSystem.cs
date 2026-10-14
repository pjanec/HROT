using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐ CE-2168 (Motion Domains MD-B, MD-C, MD-D) — moves every AIR body this node owns the position of: its <see cref="Locomotion"/>
    /// class picks the model (<see cref="MotionModelTable"/>), the body's <see cref="NavigationIntent"/> drives it — <c>Mode = Manual</c>
    /// with a <see cref="ManualDrive"/> through <see cref="IMotionModel.Manual"/>, anything else through
    /// <see cref="IMotionModel.Autonomous"/>. A ground body is not touched (no air class), and <c>LinearKinematicsSystem</c> skips
    /// every body with a <see cref="Locomotion"/>, so nothing is integrated twice.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class AirMotionSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Locomotion>()) return;
            bool intents = repo.IsComponentTypeRegistered<NavigationIntent>();
            bool drives = repo.IsComponentTypeRegistered<ManualDrive>();
            var query = repo.Query().With<Locomotion>().With<SimTransform>().WithOwned<SimTransform>().With<SimVelocity>().Build();
            foreach (var e in query)
            {
                var cls = repo.GetComponentRO<Locomotion>(e).Class;
                if (Locomotions.DomainOf(cls) != MotionDomain.Air) continue;
                var model = MotionModelTable.Model(cls);
                if (model == null) continue;

                var frame = new MotionFrame(repo, e, ref repo.GetComponentRW<SimTransform>(e), ref repo.GetComponentRW<SimVelocity>(e));
                var intent = intents && repo.HasComponent<NavigationIntent>(e) ? repo.GetComponentRO<NavigationIntent>(e) : default;
                if (intent.Mode == NavigationMode.Manual && drives && repo.HasComponent<ManualDrive>(e))
                    model.Manual(ref frame, in repo.GetComponentRO<ManualDrive>(e), deltaTime);
                else
                    model.Autonomous(ref frame, in intent, deltaTime);
            }
        }
    }

    /// <summary>
    /// ⭐ CE-2168 (MD-B) — the AIR domain's module: the systems that move air bodies (<see cref="AirMotionSystem"/>) and then what happens
    /// when one meets something (the drone's impact, M7). Held beside <c>GroundKinematicsModule</c> by the SimHost and the editor's
    /// Muscle (<c>SimHostCoreLogicPack</c>). ⚠ The per-domain OWNERSHIP role (<c>MuscleAir</c>, MD-B) is not built: the ownership table
    /// allows a component in one group only (CE-3199) — until that is decided, an air body's position stays with its creator.
    /// </summary>
    public sealed class AirMotionModule
    {
        public AirMotionModule(params IEcsModuleSystem[] after)
        {
            var list = new System.Collections.Generic.List<IEcsModuleSystem> { new AirMotionSystem() };
            if (after != null) list.AddRange(after);
            PostSimulationSystems = list;
        }

        /// <summary>Systems that run in the PostSimulation phase, in order.</summary>
        public System.Collections.Generic.IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }
    }
}
