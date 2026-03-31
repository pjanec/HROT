namespace Hrot.ExCon.Logic;

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
    // ── Editor actions ─────────────────────────────────────────────────
    /// <summary>Activate drawing edit mode for the selected tactical overlay.</summary>
    public const int EditOverlay = 100;

    /// <summary>Activate route edit mode for the selected route entity.</summary>
    public const int EditRoute = 101;

    /// <summary>Activate personal-route edit mode for the selected vehicle entity.</summary>
    public const int EditPersonalRoute = 102;

    // ── Map canvas actions ──────────────────────────────────────────────
    /// <summary>Push the distance-measurement tool onto the map canvas.</summary>
    public const int Measure = 200;}
