using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Behavior.Possession
{
    /// <summary>How a station holds a unit (C-3: one station per unit, FPS only for now).</summary>
    public enum PossessionMode : byte
    {
        None = 0,
        /// <summary>First person: the station drives the body and fires its weapon.</summary>
        Fps = 1,
    }

    /// <summary>Event ids of possession (range 5200–5299; free by a census of every event id on all lanes, <c>2026-10-11</c>).</summary>
    public static class PossessionEventIds
    {
        public const int PossessRequest = 5200;
        public const int ReleasePossession = 5201;
        public const int ControlFrame = 5202;
    }

    /// <summary>
    /// ⭐ <c>CE-3172</c> (<c>docs/DESIGN_Entity_Control.md</c> §2.3, §8b) — a station asks to possess <see cref="Unit"/>. Published on the
    /// station's own bus; the unit's BRAIN OWNER applies it (<see cref="PossessionSystem"/>), and on any other node the interaction
    /// translators carry it there (S4e), republished with <see cref="IsRemote"/> set.
    /// </summary>
    [EventId(PossessionEventIds.PossessRequest)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PossessRequestEvent
    {
        public Entity Unit;
        public PossessionMode Mode;
        /// <summary>The station — its node id (C-8: one operator per node).</summary>
        public long StationNodeId;
        public bool IsRemote;
    }

    /// <summary>⭐ <c>CE-3172</c> — the station lets the unit go; its earlier task resumes (C-4).</summary>
    [EventId(PossessionEventIds.ReleasePossession)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ReleasePossessionEvent
    {
        public Entity Unit;
        public long StationNodeId;
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3172</c> / <c>CE-3173</c> — one frame of the station's controls: the motion axes (<see cref="ManualDrive"/>'s fields),
    /// where it aims and whether it fires. The brain keeps the newest by <see cref="Seq"/> (M-6).
    /// </summary>
    [EventId(PossessionEventIds.ControlFrame)]
    [StructLayout(LayoutKind.Sequential)]
    public struct OperatorControlFrameEvent
    {
        public Entity Unit;
        public long StationNodeId;
        public uint Seq;
        public float Forward, Lateral, Steer, Yaw;
        public byte Gait;
        /// <summary>1 = the trigger is held.</summary>
        public byte Trigger;
        /// <summary>The weapon mount fired (its index; 0 = the main one).</summary>
        public byte Mount;
        /// <summary>Where the station aims (world metres).</summary>
        public Vector3 AimPoint;
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3172</c> — WHO HOLDS this unit: written by <see cref="PossessionSystem"/> only (the one writer), Brain group, read by every
    /// station (S4e replicates it). A station that sees another station's id here has lost the unit — no revoke message exists to get
    /// lost.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PossessedBy)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct PossessedBy
    {
        public long StationNodeId;
        public PossessionMode Mode;
        /// <summary>Bumped on every take-over.</summary>
        public uint Epoch;
    }

    /// <summary>⭐ <c>CE-3172</c> — the newest control frame of the possessing station, on the brain only (the <c>OperatorControl</c>
    /// behaviour reads it).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.OperatorInput)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct OperatorInput
    {
        public uint Seq;
        public float Forward, Lateral, Steer, Yaw;
        public byte Gait;
        public byte Trigger;
        public byte Mount;
        public Vector3 AimPoint;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3172</c> (M-6, M-7; <c>docs/DESIGN_Entity_Control.md</c> §8b) — the BRAIN OWNER's side of possession, the one writer of
    /// <see cref="PossessedBy"/>: a request starts the operator behaviour at origin <see cref="BehaviorOrigin.Possession"/> (which pauses
    /// the unit's task); a later station takes the unit over (C-3); frames from the holding station become <see cref="OperatorInput"/>
    /// (newest seq wins); a release — or the unit's death — clears the behaviour at origin Possession, which RESUMES the paused task
    /// (C-4), and a body left in Manual with no possession running is let go (<see cref="LetGoOfManual"/>). Events about units this
    /// node does not own are left to the translators.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateBefore(typeof(Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem))]
    public sealed class PossessionSystem : IEcsModuleSystem
    {
        private readonly string _operatorBehavior;

        /// <param name="operatorBehavior">The behaviour a possessed unit runs (the host's <c>OperatorControl</c>).</param>
        public PossessionSystem(string operatorBehavior)
        {
            _operatorBehavior = string.IsNullOrWhiteSpace(operatorBehavior)
                ? throw new System.ArgumentException("an operator behaviour name is required", nameof(operatorBehavior))
                : operatorBehavior;
        }

        /// <summary>Possessions started (a rail and diagnostics read it).</summary>
        public long Possessed { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<BehaviorState>()) return;
            Ensure(repo);

            if (repo.Bus.IsRegistered<PossessRequestEvent>())
                foreach (ref readonly var req in repo.Bus.Read<PossessRequestEvent>())
                    if (OwnsBrain(repo, req.Unit) && Alive(repo, req.Unit)) Possess(repo, req.Unit, req.StationNodeId, req.Mode);

            if (repo.Bus.IsRegistered<OperatorControlFrameEvent>())
                foreach (ref readonly var f in repo.Bus.Read<OperatorControlFrameEvent>())
                {
                    if (!OwnsBrain(repo, f.Unit) || !HeldBy(repo, f.Unit, f.StationNodeId)) continue;
                    if (repo.HasComponent<OperatorInput>(f.Unit) && f.Seq <= repo.GetComponentRO<OperatorInput>(f.Unit).Seq) continue;
                    var input = new OperatorInput
                    {
                        Seq = f.Seq, Forward = f.Forward, Lateral = f.Lateral, Steer = f.Steer, Yaw = f.Yaw, Gait = f.Gait,
                        Trigger = f.Trigger, Mount = f.Mount, AimPoint = f.AimPoint,
                    };
                    if (repo.HasComponent<OperatorInput>(f.Unit)) repo.SetComponent(f.Unit, input);
                    else repo.AddComponent(f.Unit, input);
                }

            if (repo.Bus.IsRegistered<ReleasePossessionEvent>())
                foreach (ref readonly var rel in repo.Bus.Read<ReleasePossessionEvent>())
                    if (OwnsBrain(repo, rel.Unit) && HeldBy(repo, rel.Unit, rel.StationNodeId)) Release(repo, rel.Unit);

            // C-3 / §4.3 — a killed unit is released on its own.
            foreach (var unit in repo.Query().With<PossessedBy>().Build())
                if (OwnsBrain(repo, unit) && !Alive(repo, unit)) Release(repo, unit);

            LetGoOfManual(repo);
        }

        /// <summary>
        /// ⭐ The invariant: Manual motion lives only under a running possession. A unit whose operator behaviour ended — released,
        /// killed, faulted, however — still has <see cref="NavigationMode.Manual"/> and its last axes; here its body stops and the
        /// motion request is let go, so the resumed task starts from idle. ⚠ Not a BTree deactivator: a ROOT behaviour's deactivators
        /// do not run when it is cleared (rejected in <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c>, "fire BTree
        /// deactivators on clear").
        /// </summary>
        private static void LetGoOfManual(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<ManualDrive>() || !repo.IsComponentTypeRegistered<NavigationIntent>()) return;
            foreach (var unit in repo.Query().With<ManualDrive>().With<NavigationIntent>().Build())
            {
                if (!OwnsBrain(repo, unit) || repo.GetComponentRO<BehaviorState>(unit).Origin == BehaviorOrigin.Possession) continue;
                ref var intent = ref repo.GetComponentRW<NavigationIntent>(unit);
                if (intent.Mode != NavigationMode.Manual) continue;
                intent.Mode = NavigationMode.None;
                unchecked { intent.IntentId++; }
                repo.SetComponent(unit, new ManualDrive { Yaw = repo.GetComponentRO<ManualDrive>(unit).Yaw });
            }
        }

        private void Possess(EntityRepository repo, Entity unit, long station, PossessionMode mode)
        {
            uint epoch = repo.HasComponent<PossessedBy>(unit) ? repo.GetComponentRO<PossessedBy>(unit).Epoch + 1 : 1;
            var held = new PossessedBy { StationNodeId = station, Mode = mode == PossessionMode.None ? PossessionMode.Fps : mode, Epoch = epoch };
            bool already = repo.GetComponentRO<BehaviorState>(unit).Origin == BehaviorOrigin.Possession;
            if (repo.HasComponent<PossessedBy>(unit)) repo.SetComponent(unit, held);
            else repo.AddComponent(unit, held);
            if (repo.HasComponent<OperatorInput>(unit)) repo.RemoveComponent<OperatorInput>(unit);   // the new station starts clean
            Possessed++;
            if (!already)   // a take-over keeps the running operator behaviour (and the task it paused)
                repo.Bus.PublishManaged(new AssignBehaviorEvent
                {
                    Entity = unit, BehaviorName = _operatorBehavior, JsonParams = "{}", Origin = BehaviorOrigin.Possession,
                });
        }

        private static void Release(EntityRepository repo, Entity unit)
        {
            if (repo.HasComponent<PossessedBy>(unit)) repo.RemoveComponent<PossessedBy>(unit);
            if (repo.HasComponent<OperatorInput>(unit)) repo.RemoveComponent<OperatorInput>(unit);
            repo.Bus.Publish(new ClearBehaviorEvent { Entity = unit, Origin = BehaviorOrigin.Possession });
        }

        /// <summary>The station holding <paramref name="unit"/> (null = nobody).</summary>
        public static long? HolderOf(EntityRepository repo, Entity unit)
            => repo.IsComponentTypeRegistered<PossessedBy>() && repo.HasComponent<PossessedBy>(unit)
                ? repo.GetComponentRO<PossessedBy>(unit).StationNodeId : null;

        private static bool HeldBy(EntityRepository repo, Entity unit, long station)
            => repo.HasComponent<PossessedBy>(unit) && repo.GetComponentRO<PossessedBy>(unit).StationNodeId == station;

        /// <summary>This node runs <paramref name="unit"/>'s brain — it applies possession events about it (the interaction codecs
        /// send the rest to the node that does).</summary>
        public static bool OwnsBrain(EntityRepository repo, Entity unit)
            => repo.IsAlive(unit) && repo.HasComponent<BehaviorState>(unit) && repo.HasAuthority<BehaviorState>(unit);

        private static bool Alive(EntityRepository repo, Entity unit)
            => !repo.IsComponentTypeRegistered<Health>() || !repo.HasComponent<Health>(unit) || repo.GetComponentRO<Health>(unit).Current > 0f;

        private static void Ensure(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<PossessedBy>()) repo.RegisterComponent<PossessedBy>();
            if (!repo.IsComponentTypeRegistered<OperatorInput>()) repo.RegisterComponent<OperatorInput>();
            if (!repo.Bus.IsRegistered<PossessRequestEvent>()) repo.Bus.Register<PossessRequestEvent>();
            if (!repo.Bus.IsRegistered<ReleasePossessionEvent>()) repo.Bus.Register<ReleasePossessionEvent>();
            if (!repo.Bus.IsRegistered<OperatorControlFrameEvent>()) repo.Bus.Register<OperatorControlFrameEvent>();
        }
    }
}
