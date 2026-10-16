using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — <c>Embark</c>, a library behaviour (curated C# tree, R-258):
    /// walk to the vehicle and board it. One node, <see cref="EmbarkNodes.Embark"/>. Scope: any unit with a body (a UNIT is refused by
    /// the rules — its members board, R-272).
    /// </summary>
    public static class EmbarkBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct EmbarkBlackboard
        {
            public EmbarkNodeParams Board;
        }

        /// <summary>The authored contract in (the vehicle by network id), the node's params out (the vehicle on this node).</summary>
        [BehaviorResolver(BehaviorNames.Embark)]
        public static void Resolve(in EmbarkParamsJsonDto authored, ref EmbarkBlackboard block, EntityRepository world, Entity self)
            => block.Board = new EmbarkNodeParams { Vehicle = authored.Vehicle.Resolve(world), Speed = authored.Speed };

        [Behavior(BehaviorNames.Embark, Params = typeof(EmbarkParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent | BehaviorCategory.Civilian)]   // ⭐ CE-2134 (moved off the DTO at the merge)
        [BTreeDefinition(BehaviorNames.Embark, Curated = true, Group = "Library/Basic", ParamsType = typeof(EmbarkBlackboard))]
        public static BTreeBuilder<EmbarkBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<EmbarkBlackboard, BTreeContext>()
                .Action(bb => bb.Board, EmbarkNodes.Embark);
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> (📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §4b, W-B) — <c>ManWeapon</c>, a library behaviour: walk to a crew
    /// weapon system and board its GUNNER seat — manning is boarding (W-B), the same <see cref="EmbarkNodes.Embark"/> node with
    /// <c>Role = Gunner</c>. ⭐ The seat decides the controller (<c>WeaponSystems.ControllerOf</c>: the man in its Gunner seat), so a fire
    /// mission finds the weapon through him. A plain <c>Embark</c> takes a passenger seat (a two-man mortar's loader) and controls nothing.
    /// </summary>
    public static class ManWeaponBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ManWeaponBlackboard
        {
            public EmbarkNodeParams Board;
        }

        [BehaviorResolver(BehaviorNames.ManWeapon)]
        public static void Resolve(in ManWeaponParamsJsonDto authored, ref ManWeaponBlackboard block, EntityRepository world, Entity self)
            => block.Board = new EmbarkNodeParams
            {
                Vehicle = authored.Weapon.Resolve(world), Speed = authored.Speed, Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner,
            };

        [Behavior(BehaviorNames.ManWeapon, Params = typeof(ManWeaponParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.ManWeapon, Curated = true, Group = "Library/Basic", ParamsType = typeof(ManWeaponBlackboard))]
        public static BTreeBuilder<ManWeaponBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ManWeaponBlackboard, BTreeContext>()
                .Action(bb => bb.Board, EmbarkNodes.Embark);
    }

    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — <c>Disembark</c>, a library behaviour: leave the vehicle the unit is aboard (beside the hull, at its seat).
    /// One node, <see cref="EmbarkNodes.Disembark"/>; Success at once when not aboard. The squad <c>SquadDismount</c> gives it to each
    /// member aboard (DESIGN_Squad_Tactics §5a).
    /// </summary>
    public static class DisembarkBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DisembarkBlackboard
        {
            public DisembarkNodeParams Leave;
        }

        /// <remarks>⭐ CE-1118 H-1 (Airborne Insertion §6.5 D-18) — the exit is authored (<c>{}</c> = step out, as before).</remarks>
        [BehaviorResolver(BehaviorNames.Disembark)]
        public static void Resolve(in DisembarkParamsJsonDto authored, ref DisembarkBlackboard block, EntityRepository world, Entity self)
            => block.Leave = new DisembarkNodeParams
            {
                Exit = (Fdp.Toolkit.Motion.ExitKind)authored.Exit, AnchorIndex = authored.AnchorIndex, Landing = authored.Landing,
            };

        [Behavior(BehaviorNames.Disembark, Params = typeof(DisembarkParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent | BehaviorCategory.Civilian)]   // ⭐ CE-1118 — was NoParams (CE-2134)
        [BTreeDefinition(BehaviorNames.Disembark, Curated = true, Group = "Library/Basic", ParamsType = typeof(DisembarkBlackboard))]
        public static BTreeBuilder<DisembarkBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DisembarkBlackboard, BTreeContext>()
                .Action(bb => bb.Leave, EmbarkNodes.Disembark);
    }
}

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-b (C6, R-2011; 📄 docs/DESIGN_Entity_Interactions.md §7.4.10) — <c>CarryCasualty</c>, a library behaviour (curated
    /// C# tree, R-258): walk to a man who is down, take him, bring him to a point, put him down. Three nodes in a sequence —
    /// <see cref="CarryNodes.PickUp"/>, the plain move (<see cref="CgfNodes.Action_WriteMoveToChannel"/>, slowed by the load through
    /// <c>CarryLoad</c>), <see cref="CarryNodes.PutDown"/>. Scope: a soldier type that declares the person carrier (<c>Gen.Carrier.Person</c>);
    /// the rules refuse a casualty who is not down.
    /// </summary>
    public static class CarryCasualtyBehavior
    {
        /// <summary>The arrival radius of the move to the drop point (m).</summary>
        public const float ArrivalRadius = 1.5f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct CarryCasualtyBlackboard
        {
            public PickUpNodeParams Take;
            public CgfNodes.MoveToLocationParams Bring;
            public PutDownNodeParams Leave;
        }

        /// <summary>The authored contract in (the casualty by network id), the nodes' params out.</summary>
        [BehaviorResolver(BehaviorNames.CarryCasualty)]
        public static void Resolve(in CarryCasualtyParamsJsonDto authored, ref CarryCasualtyBlackboard block, EntityRepository world, Entity self)
        {
            block.Take = new PickUpNodeParams
            {
                Casualty = authored.Casualty.Resolve(world),
                Mode = authored.Carry ? Fdp.Toolkit.Behavior.Components.CarryMode.Carried : Fdp.Toolkit.Behavior.Components.CarryMode.Dragged,
                Speed = authored.Speed,
            };
            // ⭐ An explicit speed (0 would never move: CarKinematicsSystem drives and arrives only with TargetSpeed > 0, :271 / :307);
            //   CarryLoad (C8) cuts the carrier's TOP speed by the mode, so the drag is slow without this node knowing.
            float speed = authored.Speed > 0f ? authored.Speed : CarryNodes.DefaultSpeed;
            block.Bring = new CgfNodes.MoveToLocationParams { X = authored.X, Y = authored.Y, ArrivalRadius = ArrivalRadius, Speed = speed };
            block.Leave = default;
        }

        [Behavior(BehaviorNames.CarryCasualty, Params = typeof(CarryCasualtyParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.CarryCasualty, Curated = true, Group = "Library/Infantry", ParamsType = typeof(CarryCasualtyBlackboard))]
        public static BTreeBuilder<CarryCasualtyBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CarryCasualtyBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(bb => bb.Take, CarryNodes.PickUp)
                    .Action(bb => bb.Bring, CgfNodes.Action_WriteMoveToChannel)
                    .Action(bb => bb.Leave, CarryNodes.PutDown));
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 (B10; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9) — <c>LoadCasualty</c>, a library behaviour
    /// (curated C# tree): seat the man this soldier carries in a vehicle (<see cref="CarryNodes.LoadCasualty"/>), then board it himself
    /// (<see cref="EmbarkNodes.Embark"/>). <c>SquadEmbark</c> gives it to a member who is carrying somebody.
    /// </summary>
    public static class LoadCasualtyBehavior
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct LoadCasualtyBlackboard
        {
            public LoadCasualtyNodeParams Load;
            public EmbarkNodeParams Board;
        }

        [BehaviorResolver(BehaviorNames.LoadCasualty)]
        public static void Resolve(in LoadCasualtyParamsJsonDto authored, ref LoadCasualtyBlackboard block, EntityRepository world, Entity self)
        {
            var vehicle = authored.Vehicle.Resolve(world);
            block.Load = new LoadCasualtyNodeParams { Vehicle = vehicle, Speed = authored.Speed };
            block.Board = new EmbarkNodeParams { Vehicle = vehicle, Speed = authored.Speed };
        }

        [Behavior(BehaviorNames.LoadCasualty, Params = typeof(LoadCasualtyParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.LoadCasualty, Curated = true, Group = "Library/Infantry", ParamsType = typeof(LoadCasualtyBlackboard))]
        public static BTreeBuilder<LoadCasualtyBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<LoadCasualtyBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(bb => bb.Load, CarryNodes.LoadCasualty)
                    .Action(bb => bb.Board, EmbarkNodes.Embark));
    }
}
