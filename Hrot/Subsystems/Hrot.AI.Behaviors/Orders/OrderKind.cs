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
    }
}
