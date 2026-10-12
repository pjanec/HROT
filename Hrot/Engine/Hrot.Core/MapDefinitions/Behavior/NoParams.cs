namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2140</c> — the params DTO of a behaviour that takes nothing (docs/DESIGN_Orders_Params_Entity_Classes.md §1 P1): every
    /// behaviour names exactly one params DTO, so "no params" is this empty DTO, never a missing one. Its JSON is <c>{}</c>.
    /// ⛔ An ORDER with no fields gets its own empty DTO (<c>HoldOrder</c>): the type is what identifies the order.
    /// </summary>
    public struct NoParams
    {
    }
}
