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
    }
}
