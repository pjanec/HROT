using CycloneDDS.Schema;

namespace Hrot.Gameplay;

/// <summary>A player slot's state (<see cref="PlayerSlotTopic.State"/>).</summary>
public enum PlayerSlotState : byte
{
    /// <summary>The station is observing / choosing a unit; it holds no unit.</summary>
    Choosing = 0,
    /// <summary>The server reserved a unit for the station; the station has not possessed it yet (lapses at <see cref="PlayerSlotTopic.ReservedUntilSim"/>).</summary>
    TakingControl = 1,
    /// <summary>The station possesses the unit.</summary>
    Playing = 2,
    /// <summary>⭐ S2 (D-14) — the Player's man died; <see cref="PlayerSlotTopic.RespawnAtSim"/> says when the slot goes back to Choosing (0 = never: the Player observes for good).</summary>
    Dead = 3,
}

// ⭐ DESIGN_Player_Join.md §9 (S1-a) — the gameplay wire is owned by the PACKS (GameplayWire: lazy readers/writers), not by NED translators:
//   a topic exists on the bus only where a gameplay pack is composed AND its feature is on. ⛔ Never touched by the military-sim wire (R-4102).

/// <summary>A Player station asks the deploy server for a unit (or to observe). A queue: Volatile, KeepAll.</summary>
[DdsTopic]
[DdsIdlFile("hrot-gameplay")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
public partial struct DeployRequestTopic
{
    /// <summary>The caller's id for this request; the status echoes it.</summary>
    [DdsManaged] public string RequestId;
    /// <summary>The requesting station's node id.</summary>
    public int StationNodeId;
    /// <summary>The side the station wants (<c>Fdp.Core.ForceId</c>).</summary>
    public byte Side;
    /// <summary>The unit's network id; 0 for an observer.</summary>
    public int UnitNetId;
    /// <summary>True ⇒ the station only observes (its slot is <see cref="PlayerSlotState.Choosing"/>).</summary>
    public bool Observe;
}

/// <summary>The server's answer to the station's last request. Readers filter by <see cref="RequestId"/>.</summary>
[DdsTopic]
[DdsIdlFile("hrot-gameplay")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct DeployStatusTopic
{
    [DdsKey] public int StationNodeId;
    [DdsManaged] public string RequestId;
    public bool Accepted;
    /// <summary>Why it was refused (<c>NoSuchUnit</c>, <c>NotPlayable</c>, <c>WrongSide</c>, <c>Dead</c>, <c>HeldByAnotherStation</c>, <c>ReservedForAnother</c>, <c>OtherSide</c>); empty when accepted.</summary>
    [DdsManaged] public string Reason;
    public int UnitNetId;
    /// <summary>The station that holds / has reserved the unit when it was refused for that; 0 otherwise.</summary>
    public int HolderStation;
}

/// <summary>A station's player slot (D-8): its side and the unit reserved for it. Keyed by station; disposed when the slot ends.</summary>
[DdsTopic]
[DdsIdlFile("hrot-gameplay")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct PlayerSlotTopic
{
    [DdsKey] public int StationNodeId;
    [DdsManaged] public string Name;
    public byte Side;
    /// <summary><see cref="PlayerSlotState"/> as a byte.</summary>
    public byte State;
    public int UnitNetId;
    /// <summary>Sim time (s) at which an untaken reservation lapses.</summary>
    public double ReservedUntilSim;
    /// <summary>⭐ S2 — sim time (s) a <see cref="PlayerSlotState.Dead"/> slot returns to Choosing; 0 = never (the respawn delay is 0).</summary>
    public double RespawnAtSim;
}

/// <summary>One playable unit as the server offers it to the Players (S1-b). HolderStation 0 = free.</summary>
[DdsTopic]
[DdsIdlFile("hrot-gameplay")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct DeployOfferTopic
{
    [DdsKey] public int UnitNetId;
    public byte Side;
    [DdsManaged] public string SlotName;
    public int HolderStation;
    /// <summary>⭐ S2 — <see cref="DeployOfferKind"/> as a byte: 0 a placed unit, 1 a role (a man is spawned for the Player).</summary>
    public byte Kind;
    /// <summary>A role: how many of its men are alive now.</summary>
    public int Count;
    /// <summary>A role: the most that may be alive (0 = unlimited).</summary>
    public int Max;
}

/// <summary>⭐ S2 — what a <see cref="DeployOfferTopic"/> offers. A Role's <c>UnitNetId</c> is its Deploy Role entity's network id; an IMPLICIT role (none declared on the side) is <c>-(person TKB type)</c>.</summary>
public enum DeployOfferKind : byte { Placed = 0, Role = 1 }
