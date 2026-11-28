namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2140</c> — the order vocabulary, kept in the AI assembly so users add orders here (R-277 (behaviors)). Each kind has ONE
    /// order DTO marked <c>[Order(nameof(OrderKind.X))]</c>; the behaviour that fulfils it for an entity is <c>&lt;Class&gt;&lt;Order&gt;</c>
    /// (docs/DESIGN_Orders_Params_Entity_Classes.md §1 O1–O2). ⚠ Outside this assembly orders are known by name only, through the
    /// params-DTO registry.
    /// </summary>
    public enum OrderKind
    {
        /// <summary>Go to a place: <see cref="MoveOrder"/>.</summary>
        Move = 1,
        /// <summary>Close in on and fight a target: <see cref="AttackOrder"/>.</summary>
        Attack = 2,
        /// <summary>Stay where you are: <see cref="HoldOrder"/>.</summary>
        Hold = 3,
        /// <summary>⭐ <c>CE-2173</c> (B4, R-2015) — strike a place (an aircraft first; artillery later): <see cref="StrikeOrder"/>.</summary>
        Strike = 4,
        /// <summary>⭐ <c>CE-2176</c> IF-3 (IF-E) — observe and adjust a battery's fire on a place: <see cref="ObserveFireOrder"/>.</summary>
        ObserveFire = 5,
        // ⭐⭐ CE-4612 (R-4251 R-D11 ⑤; docs/DESIGN_Orders_Params_Entity_Classes.md §15) — the kinds a unit is ORDERED to do that no order DTO
        //   issues yet (step ⑥ routes them): a concrete play declares the one it fulfils ([Behavior(Fulfils = …)], R-2089), so a reader asks
        //   CurrentOrders.Of(unit).Kind — never the behaviour's name.
        /// <summary>Hold and defend a position (<c>SquadDefend</c>, <c>PlatoonDefend</c>, <c>CompanyDefend</c>).</summary>
        Defend = 6,
        /// <summary>Move by bounds, one element covering the other (<c>BoundingOverwatch</c>, <c>PlatoonBound</c>).</summary>
        Bound = 7,
        /// <summary>Close on the enemy by fire and movement (<c>FireAndMovement</c>).</summary>
        FireAndMovement = 8,
        /// <summary>Clear a building (<c>ClearBuilding</c>).</summary>
        ClearBuilding = 9,
        /// <summary>Break contact and withdraw (<c>BreakContact</c>).</summary>
        BreakContact = 10,
        /// <summary>Recover the wounded and regroup (<c>SquadRecover</c>).</summary>
        Recover = 11,
        /// <summary>Board a vehicle (<c>SquadEmbark</c>, <c>Embark</c>).</summary>
        Embark = 12,
        /// <summary>March in formation along a route (<c>SquadMarch</c>).</summary>
        March = 13,
        /// <summary>⭐ <c>CE-4621</c> M-3 (CD-2, R-4263) — cross an exposed zone at all costs: never halt, dismount, leave the route or
        /// seek cover (<see cref="OrderConduct"/>) until the unit cannot comply (<c>BehaviorConduct.CannotComply</c>).</summary>
        Dash = 14,
        /// <summary>⭐ agg-behaviors R-8303 (B-8, ST-3) — engage from a distance and do NOT close (the base of fire): <c>SupportByFire</c>, <c>PlatoonSupportByFire</c>; a stochastic unit
        /// resolves it to its Attack play, which reads an <see cref="AttackOrder"/> with <see cref="AttackOrder.HoldBack"/>.</summary>
        SupportByFire = 15,
        /// <summary>⭐ <c>CE-9004</c> (C-9, R-9008; docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md) — drive in a road CONVOY: the lead to the destination,
        /// the rest on its path, never split (<see cref="ConvoyOrder"/>, <c>PlatformConvoy</c>).</summary>
        Convoy = 16,   // ⭐ was 15 on the convoy branch — 15 is SupportByFire (agg-behaviors, allocated first); referenced by NAME only
        /// <summary>⭐ <c>CE-4693</c> (bridge laying BL-S3, BL-11; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12) — lay a bridge across the water between two banks
        /// (<see cref="LayBridgeOrder"/>; <c>AssaultBridgeLayerLayBridge</c>, <c>FloatingBridgeEngineerLayBridge</c>). Refused, with its reason, over a gap wider than the span.</summary>
        LayBridge = 17,
        /// <summary>⭐ <c>CE-4693</c> — take a laid bridge back on board (<see cref="RecoverBridgeOrder"/>).</summary>
        RecoverBridge = 18,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-3 (CD-2, R-4263; docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §8a) — the ONE table of what each ORDER KIND
    /// forbids a unit to do: the gate compares it with a reaction's declared <see cref="Fdp.Toolkit.Behavior.Conduct"/>, never a behaviour
    /// name. ⭐ <see cref="OrderKind.Defend"/> / <see cref="OrderKind.Hold"/> forbid leaving the position — what <c>HoldsFirm</c> asked by
    /// name before (one mechanism). Every other kind forbids nothing.
    /// </summary>
    public static class OrderConduct
    {
        public static Fdp.Toolkit.Behavior.Conduct Forbids(OrderKind kind) => kind switch
        {
            OrderKind.Dash => Fdp.Toolkit.Behavior.Conduct.All,
            OrderKind.Defend or OrderKind.Hold => Fdp.Toolkit.Behavior.Conduct.LeavesRoute,
            _ => Fdp.Toolkit.Behavior.Conduct.None,
        };
    }
}
