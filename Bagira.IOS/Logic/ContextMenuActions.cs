namespace Bagira.IOS.Logic;

/// <summary>
/// Named constants for every context menu action ID.
/// Having a single authoritative place avoids magic numbers scattered across
/// <see cref="ContextMenuLogic"/> and the unit tests.
/// </summary>
public static class ContextMenuActions
{
    // ── Standard strategy ─────────────────────────────────────────────────────
    public const int CenterOnEntity = 1;
    public const int Properties     = 2;

    // ── Admin strategy ────────────────────────────────────────────────────────
    public const int Delete   = 10;
    public const int Teleport = 11;

    // ── DamageControl strategy ────────────────────────────────────────────────
    public const int Repair   = 20;
    public const int Reinforce = 21;

    // ── Logistics strategy ────────────────────────────────────────────────────
    public const int Resupply = 30;
    public const int Transfer = 31;
}
