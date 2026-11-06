using Fdp.Core;

namespace Hrot.Common;

/// <summary>
/// ⭐ Player Join D-9 (<c>docs/DESIGN_Player_Join.md</c> §9) — a scenario unit a Player may DEPLOY into: the side it belongs to and the name its slot shows. Authored on
/// the unit (the editor's Details), recorded and saved with the scenario. ⛔ Not replicated: the gameplay server owns the units and tells the Players which
/// are playable through <c>DeployOfferTopic</c>, so <c>Playable</c> never becomes a NED descriptor (the military-sim wire stays untouched, R-4102).
/// </summary>
[ComponentId(GlobalComponentIds.Playable)]
[DataPolicy(DataPolicy.Default)]   // authored world state: recorded, saved, snapshotted
public struct Playable
{
    /// <summary>The side a Player must request to take this unit.</summary>
    public ForceId Side;

    /// <summary>The name the slot shows in the deploy screen.</summary>
    public FixedString32 SlotName;
}
