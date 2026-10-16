using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The params of <see cref="WeaponSystemNodes.DeployWeapon"/>: which weapon, and which way.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DeployWeaponParams
    {
        /// <summary>The weapon system to set up or pack up.</summary>
        public Entity Weapon;
        /// <summary>0 = set it up (Deployed), 1 = pack it up (Stowed).</summary>
        public byte Pack;
        /// <summary>Runtime: 1 once this node started the change — never authored.</summary>
        [JsonIgnore] public byte Started;
    }

    /// <summary>
    /// ⭐⭐ WS-6 (📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §10) — the one new weapon-system node: <see cref="DeployWeapon"/> starts a
    /// set-up or a pack-up (<see cref="WeaponDeployment"/>, on the weapon's owner) and WAITS for the state <c>WeaponDeploySystem</c> advances.
    /// </summary>
    public static class WeaponSystemNodes
    {
        /// <summary>
        /// Success once <paramref name="p"/>'s weapon is Deployed (or Stowed when <c>Pack</c>) — at once when it already is; Running while it
        /// is on its way (whoever started it); Failure when the weapon is gone, is no weapon system, this node does not own its state, or it
        /// turned the other way.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus DeployWeapon(ref DeployWeaponParams p, Entity self, EntityRepository world)
        {
            var weapon = p.Weapon;
            if (!world.IsAlive(weapon) || !world.IsComponentTypeRegistered<WeaponSystemState>() || !world.HasComponent<WeaponSystemState>(weapon))
            {
                p.Started = 0;
                return NodeStatus.Failure;
            }
            bool pack = p.Pack != 0;
            var state = world.GetComponentRO<WeaponSystemState>(weapon).Deploy;
            if (state == (pack ? WeaponDeploy.Stowed : WeaponDeploy.Deployed)) { p.Started = 0; return NodeStatus.Success; }
            if (state == (pack ? WeaponDeploy.Packing : WeaponDeploy.Deploying)) { p.Started = 1; return NodeStatus.Running; }
            if (p.Started != 0) { p.Started = 0; return NodeStatus.Failure; }   // it was on its way and turned back
            if (!(pack ? WeaponDeployment.Pack(world, weapon) : WeaponDeployment.Deploy(world, weapon))) return NodeStatus.Failure;
            p.Started = 1;
            return NodeStatus.Running;
        }
    }

    /// <summary>
    /// ⭐⭐ WS-6 (§10, R-278 <c>&lt;Class&gt;&lt;Order&gt;</c>) — a WEAPON SYSTEM's orders, more specific than the land platform's: a weapon
    /// cannot move, so it fights from where it stands. 📌 Before: Attack resolved to <c>LandPlatformAttack</c> (the exposure cycle needs a
    /// locomotion channel ⇒ Failure) and Hold to <c>PlatformHold</c> (Idle — a sentry gun told to hold never fired).
    /// </summary>
    public static class WeaponSystemOrders
    {
        /// <summary>The order behaviours' block: the named target (Attack only) and the fire.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WeaponSystemOrderBlackboard
        {
            public DesignateParams Designate;
            public EngageParams Engage;
            public DesignateState DesignateState;
            public EngageState EngageState;
        }

        /// <summary>ATTACK: the order's target designated — one reading of it, the posture's (map lookup + warning), as <c>LandPlatformAttack</c>.</summary>
        [BehaviorResolver(BehaviorNames.WeaponSystemAttack)]
        public static void ResolveAttack(in AttackOrder order, ref WeaponSystemOrderBlackboard block, EntityRepository world, Entity self)
        {
            block = default;
            var posture = default(CombatPostureBehavior.CombatPostureBlackboard);
            CombatPostureBehavior.Resolve(new CombatPostureParamsJsonDto { Target = order.Target, Assault = true }, ref posture, world, self);
            block.Designate = posture.Designate;
        }

        /// <summary>HOLD: nothing named — the top threat, whatever comes.</summary>
        [BehaviorResolver(BehaviorNames.WeaponSystemHold)]
        public static void ResolveHold(in HoldOrder order, ref WeaponSystemOrderBlackboard block, EntityRepository world, Entity self)
            => block = default;

        /// <summary>Fire at the designated target (the threat ranking puts it first) beside its designation — Success once it is down.</summary>
        [Behavior(BehaviorNames.WeaponSystemAttack, Params = typeof(AttackOrder), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.WeaponSystemAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(WeaponSystemOrderBlackboard))]
        public static BTreeBuilder<WeaponSystemOrderBlackboard, BTreeContext> BuildAttack()
            => new BTreeBuilder<WeaponSystemOrderBlackboard, BTreeContext>()
                .Parallel(1, both => both
                    .StatefulAction(bb => bb.Designate, bb => bb.DesignateState, PostureNodes.UntilTargetDown)
                    .StatefulAction(bb => bb.Engage, bb => bb.EngageState, PostureNodes.Engage));

        /// <summary>Fire at the top threat, re-aiming when it changes; never ends (an automatic turret's standing behaviour, W-E).</summary>
        [Behavior(BehaviorNames.WeaponSystemHold, Params = typeof(HoldOrder), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.WeaponSystemHold, Curated = true, Group = "Library/Orders", ParamsType = typeof(WeaponSystemOrderBlackboard))]
        public static BTreeBuilder<WeaponSystemOrderBlackboard, BTreeContext> BuildHold()
            => new BTreeBuilder<WeaponSystemOrderBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Engage, bb => bb.EngageState, PostureNodes.Engage);
    }

    /// <summary>
    /// ⭐⭐ WS-6 (§10, §4b) — what a SOLDIER does with a weapon system, each a sequence of existing carry / embark nodes around
    /// <see cref="WeaponSystemNodes.DeployWeapon"/>: <c>SetUpWeapon</c>, <c>PackUpWeapon</c>, <c>CarryWeapon</c>.
    /// </summary>
    public static class WeaponSystemBehaviors
    {
        /// <summary>The soldier behaviours' block.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WeaponTaskBlackboard
        {
            public PutDownNodeParams PutDown;
            public EmbarkNodeParams Man;
            public DeployWeaponParams Deploy;
            public DisembarkNodeParams Leave;
            public PickUpNodeParams Carry;
        }

        private static WeaponTaskBlackboard Block(in EntityRef weapon, float speed, EntityRepository world, bool pack)
        {
            var w = weapon.Resolve(world);
            return new WeaponTaskBlackboard
            {
                Man = new EmbarkNodeParams { Vehicle = w, Speed = speed, Role = SeatRole.Gunner },
                Deploy = new DeployWeaponParams { Weapon = w, Pack = pack ? (byte)1 : (byte)0 },
                Carry = new PickUpNodeParams { Casualty = w, Mode = CarryMode.Carried, Speed = speed },
            };
        }

        [BehaviorResolver(BehaviorNames.SetUpWeapon)]
        public static void ResolveSetUp(in SetUpWeaponParamsJsonDto authored, ref WeaponTaskBlackboard block, EntityRepository world, Entity self)
            => block = Block(authored.Weapon, authored.Speed, world, pack: false);

        [BehaviorResolver(BehaviorNames.PackUpWeapon)]
        public static void ResolvePackUp(in PackUpWeaponParamsJsonDto authored, ref WeaponTaskBlackboard block, EntityRepository world, Entity self)
            => block = Block(authored.Weapon, authored.Speed, world, pack: true);

        [BehaviorResolver(BehaviorNames.CarryWeapon)]
        public static void ResolveCarry(in CarryWeaponParamsJsonDto authored, ref WeaponTaskBlackboard block, EntityRepository world, Entity self)
            => block = Block(authored.Weapon, authored.Speed, world, pack: false);

        /// <summary>Put it down (Success at once when carrying nothing) · walk to it and take its Gunner seat · set it up and wait.</summary>
        [Behavior(BehaviorNames.SetUpWeapon, Params = typeof(SetUpWeaponParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.SetUpWeapon, Curated = true, Group = "Library/Basic", ParamsType = typeof(WeaponTaskBlackboard))]
        public static BTreeBuilder<WeaponTaskBlackboard, BTreeContext> BuildSetUp()
            => new BTreeBuilder<WeaponTaskBlackboard, BTreeContext>()
                .Sequence(steps => steps
                    .Action(bb => bb.PutDown, CarryNodes.PutDown)
                    .Action(bb => bb.Man, EmbarkNodes.Embark)
                    .Action(bb => bb.Deploy, WeaponSystemNodes.DeployWeapon));

        /// <summary>Pack it up and wait (Stowed) · step off it (Success at once when not aboard).</summary>
        [Behavior(BehaviorNames.PackUpWeapon, Params = typeof(PackUpWeaponParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.PackUpWeapon, Curated = true, Group = "Library/Basic", ParamsType = typeof(WeaponTaskBlackboard))]
        public static BTreeBuilder<WeaponTaskBlackboard, BTreeContext> BuildPackUp()
            => new BTreeBuilder<WeaponTaskBlackboard, BTreeContext>()
                .Sequence(steps => steps
                    .Action(bb => bb.Deploy, WeaponSystemNodes.DeployWeapon)
                    .Action(bb => bb.Leave, EmbarkNodes.Disembark));

        /// <summary>Walk to it and pick it up — the casualty's <c>PickUp</c>: <c>Embarkation.TryEmbark</c> boards a portable weapon onto the
        /// carry station (a two-man weapon needs the team carry, T-c — refused here).</summary>
        [Behavior(BehaviorNames.CarryWeapon, Params = typeof(CarryWeaponParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.CarryWeapon, Curated = true, Group = "Library/Basic", ParamsType = typeof(WeaponTaskBlackboard))]
        public static BTreeBuilder<WeaponTaskBlackboard, BTreeContext> BuildCarry()
            => new BTreeBuilder<WeaponTaskBlackboard, BTreeContext>()
                .Action(bb => bb.Carry, CarryNodes.PickUp);
    }
}
