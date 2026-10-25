using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2375</c> (AQ104 A-1, R-2081) — a lone man's AREA order makes him a real squad. <see cref="Request"/> asks for a squad entity
    /// through the creation request (not transient: it is saved like an authored one) and marks the man <see cref="PendingUnitCommand"/>;
    /// <see cref="OneManCommandSystem"/> assigns him under it and gives it the play once it exists on this node. 🔒 User, 2026-10-25:
    /// <i>"creating an entity is a cross cluster operation … nothing local and immediate"</i> — so the man fights on the order's fallback while
    /// he waits. 📄 docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md §7.3a.
    /// </summary>
    public static class OneManCommand
    {
        /// <summary>The squad's name: the man's own and this — the key the brain recognises the new squad by (no creation result reaches it).</summary>
        public const string NameSuffix = " (command)";

        /// <summary>A man with no commander and no men under him: a lone man.</summary>
        public static bool IsLone(EntityRepository world, Entity man)
            => !(world.IsComponentTypeRegistered<UnitSubordinate>() && world.HasComponent<UnitSubordinate>(man) && !world.GetComponentRO<UnitSubordinate>(man).Commander.IsNull)
               && !(world.IsComponentTypeRegistered<UnitRoster>() && world.HasComponent<UnitRoster>(man) && world.GetComponentRO<UnitRoster>(man).Count > 0);

        /// <summary>Asks for <paramref name="man"/>'s squad, which will run <paramref name="play"/> with <paramref name="paramsJson"/>. False when
        /// this node cannot (no creation request on its bus, no name, no position) or he is already waiting for one.</summary>
        public static bool Request(EntityRepository world, Entity man, string play, string paramsJson)
        {
            if (!world.Bus.IsRegisteredManaged<EntityCreationRequest>() || !world.HasComponent<SimTransform>(man) || !world.HasComponent<EntityInfo>(man)) return false;
            if (world.HasManagedComponent<PendingUnitCommand>(man)) return false;
            var info = world.GetComponentRO<EntityInfo>(man);
            string name = SquadNameOf(info.Name);
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(man);
            world.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,   // the system picks: the brain node
                TkbType = Hrot.Map.Common.TkbEntityTypes.Unit_InfantrySquad,
                InitType = Fdp.Toolkit.Replication.ReliableInitType.None,   // nobody needs to ACK a squad node
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = tf.Position, Rotation = Quaternion.Identity },
                    new EntityInfo { Name = name, ForceId = info.ForceId },
                },
            });
            world.SetManagedComponent(man, new PendingUnitCommand { SquadName = name, Play = play, ParamsJson = paramsJson, AskedAt = Now(world) });
            return true;
        }

        public static string SquadNameOf(string man)
        {
            string name = (man ?? "") + NameSuffix;
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        internal static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }

    /// <summary>
    /// ⭐ <c>CE-2375</c> (AQ104 A-1) — on the brain node: each man waiting for his squad (<see cref="PendingUnitCommand"/>) — once a squad
    /// entity of his name, his force and no members exists here, he is assigned under it (squad leader) and it gets its standing orders and
    /// the play (Superior); the play's member order then replaces his fallback. No squad in <see cref="GiveUpSeconds"/> ⇒ he stays on the
    /// fallback (never a second request — two squads for one man).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class OneManCommandSystem : IEcsModuleSystem
    {
        public const double GiveUpSeconds = 20.0;
        private readonly bool _gateOnAuthority;
        private readonly List<Entity> _done = new();

        public OneManCommandSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>Squads assigned since construction (a rail / diagnostics probe).</summary>
        public int Assigned { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository world) return;   // PendingUnitCommand: CognitiveComponentRegistry
            double now = OneManCommand.Now(world);
            _done.Clear();
            foreach (var man in world.Query().WithManaged<PendingUnitCommand>().Build())
            {
                if (_gateOnAuthority && !(world.HasComponent<BehaviorState>(man) && world.HasAuthority<BehaviorState>(man))) continue;
                var pending = ((ISimulationView)world).GetManagedComponentRO<PendingUnitCommand>(man);
                var squad = Find(world, man, pending.SquadName);
                if (squad.IsNull)
                {
                    if (now - pending.AskedAt >= GiveUpSeconds) _done.Add(man);
                    continue;
                }
                if (world.Bus.IsRegistered<CmdAssignSubordinate>())
                    world.Bus.Publish(new CmdAssignSubordinate { Subordinate = man, Commander = squad, Designation = TacticalDesignation.SquadLeader });
                if (world.Bus.IsRegisteredManaged<AssignSopEvent>())
                    world.Bus.PublishManaged(new AssignSopEvent { Entity = squad, BehaviorName = "BasicSquadSop", JsonParams = "{}", Origin = BehaviorOrigin.Superior });
                if (world.Bus.IsRegisteredManaged<AssignBehaviorEvent>())
                    world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = squad, BehaviorName = pending.Play, JsonParams = pending.ParamsJson, Origin = BehaviorOrigin.Superior });
                Assigned++;
                _done.Add(man);
            }
            foreach (var man in _done) world.RemoveComponent<PendingUnitCommand>(man);
        }

        /// <summary>The squad entity named <paramref name="name"/>, of his force, with no members yet.</summary>
        private static Entity Find(EntityRepository world, Entity man, string name)
        {
            var force = world.GetComponentRO<EntityInfo>(man).ForceId;
            foreach (var e in world.Query().With<EntityInfo>().With<TkbIdentity>().Build())
            {
                if (e == man || world.GetComponentRO<TkbIdentity>(e).TkbType != Hrot.Map.Common.TkbEntityTypes.Unit_InfantrySquad) continue;
                ref readonly var info = ref world.GetComponentRO<EntityInfo>(e);
                if (info.ForceId != force || (string)info.Name != name) continue;
                if (world.HasComponent<UnitRoster>(e) && world.GetComponentRO<UnitRoster>(e).Count > 0) continue;
                return e;
            }
            return Entity.Null;
        }
    }
}
