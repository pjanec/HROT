using System;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Common.Infrastructure;

/// <summary>⭐ Seats and hats (📄 docs/UX/DESIGN_Seats_And_Hats.md §2.2, R-4131) — who a station is: it bounds what the station may do.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Seat : byte
{
    Author = 0,
    Instructor = 1,
    SideController = 2,
    Player = 3,
    Observer = 4,
    Reviewer = 5,
}

/// <summary>The config of <c>station.seat</c>: the seat and, for a side controller / a player, its side (empty = all).
/// ⛔ Unmanaged (F-21): <see cref="Side"/> is a <see cref="FixedString32"/> — a <c>string</c> defaults to null, which the relaxed JSON
/// options OMIT when writing, so the overlay's known-key check refused every layer naming <c>Side</c> and the seat never left its default.</summary>
public struct SeatConfig : IFeatureConfig
{
    public bool Enabled { get; set; }
    public Seat Seat { get; set; }
    public FixedString32 Side { get; set; }
}

/// <summary>
/// ⭐ Seats and hats H1 (📄 docs/UX/DESIGN_Seats_And_Hats.md §2.4, Q4) — the seat is ONE node feature, <c>station.seat</c>, declared on EVERY host by the shared
/// bootstrapper with the host's default (the editor Author, the fused server and CGF Instructor, the Player Player); a launch flag
/// (<c>--feature station.seat={"Seat":"Observer"}</c>) or the scenario's preset overrides it. Cluster-visible, kept in the recording (it is a feature),
/// rarely changed. ⭐ Read by the edit gate (<c>HrotEditGate.LevelOf</c>) and — H2 — the action catalogue.
/// </summary>
public static class StationSeat
{
    public const string Id = "station.seat";

    /// <summary>The declaration, defaulting to <paramref name="defaultSeat"/>.</summary>
    public static FeatureDeclaration Declaration(Seat defaultSeat)
        => FeatureDeclaration.Of<SeatConfig>(Id, "Who this station is (the seat): Author, Instructor, Side controller, Player, Observer or Reviewer — it bounds what the station may edit and command",
            FeatureChange.Live, _ => "{\"Enabled\":true,\"Seat\":\"" + defaultSeat + "\",\"Side\":\"\"}");

    /// <summary>The station's seat (Author when the node does not declare it).</summary>
    public static Seat Of(NodeFeatures? features)
        => features != null && features.Registry.Available(Id) ? features.Config<SeatConfig>(features.Resolve(Id)).Seat : Seat.Author;
}
