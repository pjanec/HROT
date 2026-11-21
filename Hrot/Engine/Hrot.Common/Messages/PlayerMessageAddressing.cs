using Fdp.Core;

namespace Hrot.Common.Messages;

/// <summary>
/// Who is looking: the possessed unit's network id and side, and the station (⭐ CE-11027 S12-4: a <see cref="Fdp.Core.StationId"/> — node AND seat index — that posts orders as this viewer).
/// </summary>
public readonly record struct MessageViewer(int UnitNetId, ForceId Side, StationId StationId);

/// <summary>
/// ⭐⭐ CE-11027 S12-4 (Q114 I; docs/DESIGN_Player_Join.md §12.4) — THE addressing rule of a player message: ONE place the map's log (<c>PlayerMessages</c>, Presentation) and the gateway's per-seat
/// <c>PlayerMessage</c> both ask, below Presentation (the gateway cannot reference it). Moved here from <c>PlayerMessages.Addressed</c>; ⛔ no second copy.
/// </summary>
public static class PlayerMessageAddressing
{
    /// <summary>
    /// Is a message to (<paramref name="kind"/>, <paramref name="value"/>) for <paramref name="viewer"/>? Instructor-addressed ones never are, and an unknown kind matches nothing.
    /// A <c>Station</c> address is the station's WHOLE id (node low 16 bits, seat index high 16): seat 0 of node 5 is <c>5</c>, seat 2 is <c>5 | 2 &lt;&lt; 16</c> — each reaches only its own seat.
    /// </summary>
    public static bool Addressed(string kind, int value, MessageViewer viewer) => kind switch
    {
        "All" => true,
        "Unit" => viewer.UnitNetId != 0 && value == viewer.UnitNetId,
        "Side" => value == (int)viewer.Side,
        "Station" => viewer.StationId.Value != 0 && viewer.StationId == value,
        _ => false,   // "Instructor" and anything else
    };
}
