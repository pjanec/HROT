using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ Weapon systems WS-1 (R-2016, docs/designs/weapons/DESIGN_Weapon_Systems.md §3, §5) — how a weapon-system entity is handled,
    /// projected from its TKB <see cref="WeaponSystemDto"/> (<c>Gen.WeaponSystem</c>) on every node. Its presence MAKES the entity a weapon
    /// system (<c>WeaponSystems.IsWeaponSystem</c>). A type fact: never saved, never replicated.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponSystemParams)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct WeaponSystemParams
    {
        /// <summary>1 when it fires only while someone operates it; 0 = AUTOMATIC (W-E).</summary>
        public byte NeedsOperator;
        /// <summary><see cref="WeaponPortability"/>.</summary>
        public byte Portable;
        public float CarrySpeedFactor;
        /// <summary>1 when it fires while carried (stowed).</summary>
        public byte FireWhileCarried;
        public float DeploySeconds;
        public float PackSeconds;
        /// <summary><see cref="MountKinds"/>.</summary>
        public byte MountKinds;
        /// <summary>⭐ <c>CE-3233</c> (B-11b) — 1 for an explosive charge: detonated, never fired.</summary>
        public byte Charge;

        public static WeaponSystemParams From(WeaponSystemDto d) => new()
        {
            NeedsOperator = (byte)(d.NeedsOperator ? 1 : 0), Portable = (byte)d.Portable, CarrySpeedFactor = d.CarrySpeedFactor,
            FireWhileCarried = (byte)(d.FireWhileCarried ? 1 : 0), DeploySeconds = d.DeploySeconds, PackSeconds = d.PackSeconds,
            MountKinds = (byte)d.MountKinds, Charge = (byte)(d.Charge ? 1 : 0),
        };
    }

    /// <summary>⭐ WS-1 (W-C) — a weapon system's deploy state.</summary>
    public enum WeaponDeploy : byte
    {
        /// <summary>Transport position: it can be carried; it does not fire unless <c>FireWhileCarried</c>.</summary>
        Stowed = 0,
        /// <summary>Being set up, until <see cref="WeaponSystemState.Until"/>.</summary>
        Deploying = 1,
        /// <summary>Ready to fire.</summary>
        Deployed = 2,
        /// <summary>Being packed up, until <see cref="WeaponSystemState.Until"/>.</summary>
        Packing = 3,
    }

    /// <summary>
    /// ⭐⭐ WS-1 (W-C, R-136) — a weapon system's deploy state: written by the node owning <c>dtWeaponSystemState</c> (the weapon's brain
    /// group, <see cref="WeaponDeploySystem"/>), replicated to every node, saved with the scenario. The drawing and the fire gate both read it.
    /// At ease / Ready / Aimed stays the GUNNER's weapon posture (R-272).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponSystemState)]
    public struct WeaponSystemState
    {
        public WeaponDeploy Deploy;
        /// <summary>Sim time (<c>GlobalTime.TotalTime</c>, s) a <see cref="WeaponDeploy.Deploying"/> or <see cref="WeaponDeploy.Packing"/> ends.</summary>
        public double Until;
    }

    /// <summary>
    /// ⭐ WS-1 — projects a weapon-system type's <see cref="WeaponSystemDto"/>: <see cref="WeaponSystemParams"/> (a type fact, always rewritten)
    /// and, the first time only, <see cref="WeaponSystemState"/> <see cref="WeaponDeploy.Deployed"/> — a placed weapon stands ready (a loaded
    /// scenario keeps the state it saved; a carried one is stowed by whoever picks it up).
    /// </summary>
    public sealed class WeaponSystemTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(WeaponSystemDto); }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(WeaponSystemParams);
            yield return typeof(WeaponSystemState);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<WeaponSystemDto>();
            if (dto == null) return;
            if (repo.IsComponentTypeRegistered<WeaponSystemParams>())
            {
                var p = WeaponSystemParams.From(dto);
                if (repo.HasComponent<WeaponSystemParams>(entity)) repo.SetComponent(entity, p);
                else repo.AddComponent(entity, p);
            }
            if (repo.IsComponentTypeRegistered<WeaponSystemState>() && !repo.HasComponent<WeaponSystemState>(entity))
                repo.AddComponent(entity, new WeaponSystemState { Deploy = WeaponDeploy.Deployed });
        }
    }

    /// <summary>
    /// ⭐⭐ WS-1 (W-C) — advances a weapon system's deploy state on the node that OWNS it (<c>dtWeaponSystemState</c>; one node, the editor,
    /// owns all): <see cref="WeaponDeploy.Deploying"/> ⇒ <see cref="WeaponDeploy.Deployed"/> and <see cref="WeaponDeploy.Packing"/> ⇒
    /// <see cref="WeaponDeploy.Stowed"/> once their time is up. The behaviours start them (<see cref="WeaponDeployment.Deploy"/>,
    /// <see cref="WeaponDeployment.Pack"/>). Composed on every node by <c>CoreInfrastructureCapabilities.Interactions</c>.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class WeaponDeploySystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<WeaponSystemState>()) return;
            double now = WeaponDeployment.Now(repo);
            foreach (var e in repo.Query().With<WeaponSystemState>().Build())
            {
                var s = repo.GetComponentRO<WeaponSystemState>(e);
                if (s.Deploy is not (WeaponDeploy.Deploying or WeaponDeploy.Packing) || now < s.Until) continue;
                if (!WeaponDeployment.Owns(repo, e)) continue;
                repo.GetComponentRW<WeaponSystemState>(e) = new WeaponSystemState
                {
                    Deploy = s.Deploy == WeaponDeploy.Deploying ? WeaponDeploy.Deployed : WeaponDeploy.Stowed,
                };
            }
        }
    }

    /// <summary>⭐ WS-1 (W-C) — starting a deploy or a pack-up, on the weapon's owner (the behaviours' <c>SetUpWeapon</c> /
    /// <c>PackUpWeapon</c>, WS-6).</summary>
    public static class WeaponDeployment
    {
        /// <summary>⭐ NED's <c>dtWeaponSystemState</c>: the deploy state is published under this key and only its owner writes it. A rail pins
        /// it to <c>EDescriptorType</c>.</summary>
        public const long WeaponSystemStateDescriptorOrdinal = 132;

        public static readonly long StateKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(WeaponSystemStateDescriptorOrdinal, 0);

        /// <summary>Does this node write <paramref name="weapon"/>'s deploy state? (No <c>NetworkAuthority</c> — one node — always.)</summary>
        public static bool Owns(EntityRepository repo, Entity weapon)
            => Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, weapon, StateKey);

        /// <summary>Starts setting <paramref name="weapon"/> up (Stowed or Packing ⇒ Deploying for its <c>DeploySeconds</c>). False when this
        /// node does not own it, it is no weapon system, or it is already deploying / deployed.</summary>
        public static bool Deploy(EntityRepository repo, Entity weapon) => Begin(repo, weapon, deploy: true);

        /// <summary>Starts packing <paramref name="weapon"/> up (Deployed or Deploying ⇒ Packing for its <c>PackSeconds</c>).</summary>
        public static bool Pack(EntityRepository repo, Entity weapon) => Begin(repo, weapon, deploy: false);

        /// <summary>The sim clock the deploy times run on.</summary>
        public static double Now(EntityRepository repo) => repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;

        /// <summary>
        /// ⭐ <c>CE-3214</c> (hit location §11 S2b-7) — how far a weapon is deployed (0 stowed … 1 deployed) at <paramref name="now"/>: a deploy
        /// rises over its last <paramref name="seconds"/> before <paramref name="until"/>, a pack-up falls. The ONE formula: the drawing
        /// (ui's <c>WeaponKits.Deployment</c>) and the hit test both read it.
        /// </summary>
        public static float Fraction(WeaponDeploy deploy, double until, double now, float seconds)
        {
            float left = seconds > 0f ? Math.Clamp((float)((until - now) / seconds), 0f, 1f) : 0f;
            return deploy switch
            {
                WeaponDeploy.Deployed => 1f,
                WeaponDeploy.Deploying => 1f - left,
                WeaponDeploy.Packing => left,
                _ => 0f,
            };
        }

        /// <summary>⭐ <c>CE-3214</c> — how far <paramref name="weapon"/> is deployed now, from its replicated state; 1 without one.</summary>
        public static float FractionOf(EntityRepository repo, Entity weapon)
        {
            if (!repo.IsComponentTypeRegistered<WeaponSystemState>() || !repo.HasComponent<WeaponSystemState>(weapon)) return 1f;
            var state = repo.GetComponentRO<WeaponSystemState>(weapon);
            float seconds = 0f;
            if (repo.IsComponentTypeRegistered<WeaponSystemParams>() && repo.HasComponent<WeaponSystemParams>(weapon))
            {
                var p = repo.GetComponentRO<WeaponSystemParams>(weapon);
                seconds = state.Deploy == WeaponDeploy.Packing ? p.PackSeconds : p.DeploySeconds;
            }
            return Fraction(state.Deploy, state.Until, Now(repo), seconds);
        }

        private static bool Begin(EntityRepository repo, Entity weapon, bool deploy)
        {
            if (!repo.IsAlive(weapon) || !repo.IsComponentTypeRegistered<WeaponSystemState>() || !repo.HasComponent<WeaponSystemState>(weapon)
                || !repo.HasComponent<WeaponSystemParams>(weapon) || !Owns(repo, weapon)) return false;
            var now = repo.GetComponentRO<WeaponSystemState>(weapon).Deploy;
            if (deploy ? now is WeaponDeploy.Deploying or WeaponDeploy.Deployed : now is WeaponDeploy.Packing or WeaponDeploy.Stowed) return false;
            var p = repo.GetComponentRO<WeaponSystemParams>(weapon);
            repo.GetComponentRW<WeaponSystemState>(weapon) = new WeaponSystemState
            {
                Deploy = deploy ? WeaponDeploy.Deploying : WeaponDeploy.Packing,
                Until = Now(repo) + Math.Max(0f, deploy ? p.DeploySeconds : p.PackSeconds),
            };
            return true;
        }
    }
}
