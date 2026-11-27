using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Hrot.Common.Orders;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-11041</c> (docs/DESIGN_Entity_Control.md §8h.10) — <see cref="DriveHandOver"/> on its own, with NO station and NO presentation:
/// the three steps (Riding after the replicated driver seat shows, the vehicle after Riding replicates, done once it is held), Get out, the
/// timeout, the orphan rider — all from replicated state and the <see cref="EntityOrders"/> a remote seat has too.
/// </summary>
public sealed partial class PossessionStationTests
{
    private static DriveHandOver HandOverFor(UseWorld u) => new(() => u.W, () => new EntityOrders(u.W, u.Map, geo: null));

    private static PossessionMode ModeOf(UseWorld u, Entity e) => u.W.GetComponentRO<PossessedBy>(e).Mode;

    /// <summary>The three steps from replicated state alone, then Get out hands back to the soldier. Invariant after every step: at most
    /// ONE Fps unit is held, and the vehicle is never possessed before the soldier rides.</summary>
    [Fact]
    public void CE11041_DriveHandOver_ThreeSteps_FromReplicatedStateAlone_ThenGetOut()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        var h = HandOverFor(u);
        h.Arm(u.Me, bmp);
        Assert.Equal(DriveHandOver.HandOverStep.AwaitBoard, h.Current);

        h.Step(0.016f);                                                          // not aboard: nothing is asked
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Fps, ModeOf(u, u.Me));
        Assert.Equal(DriveHandOver.HandOverStep.AwaitBoard, h.Current);

        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, u.Me, bmp, SeatRole.Driver));
        h.Step(0.016f);                                                          // the driver seat shows: Riding is asked
        Assert.Equal(DriveHandOver.HandOverStep.AwaitRiding, h.Current);
        h.Step(0.016f);                                                          // his Riding has not replicated: the vehicle waits
        u.ApplyPossession(swap: true);
        Assert.False(u.W.HasComponent<PossessedBy>(bmp));
        Assert.Equal(PossessionMode.Riding, ModeOf(u, u.Me));

        h.Step(0.016f);                                                          // he rides: the vehicle is asked
        Assert.Equal(DriveHandOver.HandOverStep.AwaitVehicle, h.Current);
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Riding, ModeOf(u, u.Me));                    // the vehicle's possession kept him
        h.Step(0.016f);
        Assert.Equal(DriveHandOver.HandOverStep.None, h.Current);
        Assert.Equal(bmp, h.HeldHere(out int id));
        Assert.Equal(88, id);
        Assert.Equal(u.Me, DriveHandOver.RiderOf(u.W, bmp));
        Assert.False(h.Handing);

        Assert.True(h.TryGetOut(bmp));                                           // Get out: back into him
        Assert.Equal(u.Me, h.GetOutRider);
        Assert.True(h.Handing);
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Fps, ModeOf(u, u.Me));
        Assert.False(u.W.HasComponent<PossessedBy>(bmp));                        // one Fps unit per station: the vehicle let go
        Assert.Equal(u.Me, h.HeldHere(out _));
        h.GetOutSent();
        Assert.False(h.Handing);
    }

    /// <summary>Get out of a vehicle with no rider is false and asks for nothing.</summary>
    [Fact]
    public void CE11041_DriveHandOver_GetOut_WithNoRider_IsFalse()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        Assert.False(HandOverFor(u).TryGetOut(bmp));
    }

    /// <summary>A step that does not move on is dropped after <see cref="DriveHandOver.HandOverTimeout"/>, and not before; an unreleased
    /// Get-out wait ends the same way.</summary>
    [Fact]
    public void CE11041_DriveHandOver_TimesOut_AtTheTimeout_NotBefore()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        var h = HandOverFor(u);
        h.Arm(u.Me, bmp);
        h.Step(DriveHandOver.HandOverTimeout - 1f);
        Assert.Equal(DriveHandOver.HandOverStep.AwaitBoard, h.Current);          // still waiting for a seat that never comes
        h.Step(2f);
        Assert.Equal(DriveHandOver.HandOverStep.None, h.Current);

        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, u.Me, bmp, SeatRole.Driver));
        h.Step(0.016f);                                                          // idle: nothing re-armed itself
        Assert.Equal(DriveHandOver.HandOverStep.None, h.Current);
    }

    /// <summary>A soldier released meanwhile (Esc) ends the hand-over; a vehicle destroyed ends it too.</summary>
    [Fact]
    public void CE11041_DriveHandOver_ReleasedMeanwhile_Ends()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        var h = HandOverFor(u);
        h.Arm(u.Me, bmp);
        u.W.RemoveComponent<PossessedBy>(u.Me);
        h.Step(0.016f);
        Assert.Equal(DriveHandOver.HandOverStep.None, h.Current);
    }

    /// <summary>A rider whose vehicle the station no longer drives is let go within a second; one it still drives is kept.</summary>
    [Fact]
    public void CE11041_DriveHandOver_ReleasesAnOrphanRider_KeepsOneAboardAHeldVehicle()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        var h = HandOverFor(u);
        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, u.Me, bmp, SeatRole.Driver));
        u.W.SetComponent(u.Me, new PossessedBy { StationNodeId = (Fdp.Core.StationId)0, Mode = PossessionMode.Riding, Epoch = 2 });
        u.W.AddComponent(bmp, new PossessedBy { StationNodeId = (Fdp.Core.StationId)0, Mode = PossessionMode.Fps, Epoch = 3 });

        h.Step(1.1f);
        u.ApplyPossession();
        Assert.Equal(PossessionMode.Riding, ModeOf(u, u.Me));                    // the vehicle is driven here: he stays

        u.W.RemoveComponent<PossessedBy>(bmp);
        h.Step(1.1f);
        u.ApplyPossession();
        Assert.False(u.W.HasComponent<PossessedBy>(u.Me));                       // nobody drives the vehicle: he is let go
    }

    /// <summary>The station identity is <see cref="EntityOrders.StationFor"/>: a unit held by ANOTHER station is not this one's, so a seat
    /// never sees a vehicle someone else drives as its own.</summary>
    [Fact]
    public void CE11041_DriveHandOver_HeldHere_IsThisStationsUnitOnly()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        u.W.AddComponent(bmp, new PossessedBy { StationNodeId = (Fdp.Core.StationId)7, Mode = PossessionMode.Fps, Epoch = 9 });
        var h = HandOverFor(u);
        Assert.Equal(u.Me, h.HeldHere(out _));                                   // the newer Fps of ANOTHER station does not count
    }

    /// <summary>⭐ C3 CC-10 (docs/DESIGN_Capability_Components.md §4.3) — <see cref="StationBody"/>, the one owner of the body: riding his vehicle,
    /// the station DRIVES the vehicle (camera) while its OWN body is the man (side, briefing); another station's newer hold never counts; and
    /// <see cref="StationClaims.TrySideOf"/> takes the side from the own body. 🔴 Mutation: <c>Own</c> returning <c>Driven</c> ⇒ the side is the vehicle's.</summary>
    [Fact]
    public void C3_StationBody_DrivesTheVehicle_WhileTheOwnBodyIsTheRider_AndNamesTheSide()
    {
        using var u = new UseWorld();
        var bmp = u.Vehicle(new Vector3(2f, 0f, 0f), 88);
        var theirs = u.Vehicle(new Vector3(9f, 0f, 0f), 89);
        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, u.Me, bmp, SeatRole.Driver));
        u.W.SetComponent(u.Me, new PossessedBy { StationNodeId = (Fdp.Core.StationId)0, Mode = PossessionMode.Riding, Epoch = 2 });
        u.W.AddComponent(bmp, new PossessedBy { StationNodeId = (Fdp.Core.StationId)0, Mode = PossessionMode.Fps, Epoch = 3 });
        u.W.AddComponent(theirs, new PossessedBy { StationNodeId = (Fdp.Core.StationId)7, Mode = PossessionMode.Fps, Epoch = 9 });
        if (!u.W.IsComponentTypeRegistered<EntityInfo>()) u.W.RegisterComponent<EntityInfo>();
        if (u.W.HasComponent<EntityInfo>(u.Me)) u.W.SetComponent(u.Me, new EntityInfo { ForceId = ForceId.Friend }); else u.W.AddComponent(u.Me, new EntityInfo { ForceId = ForceId.Friend });
        if (u.W.HasComponent<EntityInfo>(bmp)) u.W.SetComponent(bmp, new EntityInfo { ForceId = ForceId.Neutral }); else u.W.AddComponent(bmp, new EntityInfo { ForceId = ForceId.Neutral });

        Assert.Equal(bmp, StationBody.Driven(u.W, 0));                 // the camera goes into the vehicle he drives
        Assert.Equal(u.Me, StationBody.Own(u.W, 0));                   // ...while he is still himself
        Assert.Equal(theirs, StationBody.Driven(u.W, 7));              // station 7 sees only its own hold
        Assert.True(StationClaims.TrySideOf(u.W, 0, out var side));
        Assert.Equal(ForceId.Friend, side);                            // the man's side, not the vehicle's
    }
}
