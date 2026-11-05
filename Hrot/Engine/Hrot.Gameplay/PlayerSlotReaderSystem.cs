using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Possession;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join §9 (D-8, S1-c) — fills this world's <see cref="StationReservations"/> from <see cref="PlayerSlotTopic"/>: a live slot ⇒ <c>Set(station, side, unit)</c>
/// (a Choosing slot holds a side only, unit 0); a slot that ended ⇒ <c>Remove(station)</c>. ⭐ ONE reader class, composed by the server pack (as the gated
/// <see cref="ServerPlayerSlotReaderSystem"/>) AND the client pack — the Brain's <c>StationClaims</c> and the Player's own claim tests then read the SAME reservations.
/// A slot ends when its sample is DISPOSED (the server ended it) or its writer is GONE (the server left): both leave an invalid sample, and both clear the reservation.
/// </summary>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.EveryFrame)]
public class PlayerSlotReaderSystem : IEcsModuleSystem
{
    private readonly GameplayWire _wire;

    public PlayerSlotReaderSystem(GameplayWire wire) => _wire = wire ?? throw new ArgumentNullException(nameof(wire));

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        var reader = _wire.SlotReader;
        if (reader is null) return;
        using var loan = reader.Take();
        foreach (var s in loan)
        {
            if (s.IsValid)
            {
                var slot = s.Data;
                StationReservations.Of(repo).Set(slot.StationNodeId, (ForceId)slot.Side, slot.State == (byte)PlayerSlotState.Choosing ? 0 : slot.UnitNetId);
            }
            else if (s.Info.InstanceState != DdsInstanceState.Alive)
            {
                var key = DdsTypeSupport.FromNative<PlayerSlotTopic>(s.NativePtr);
                StationReservations.Of(repo).Remove(key.StationNodeId);
            }
        }
    }
}

/// <summary>
/// The server pack's reader: the same class behind the <c>gameplay.deploy-server</c> feature gate. ⭐ A SUBCLASS because the Player's client pack composes the
/// plain reader on a node that does not declare that feature — a <c>[NodeFeature]</c> naming an undeclared id throws at registration (F-18).
/// </summary>
[UpdateInPhase(SystemPhase.Input)]
[TickMode(TickPolicy.EveryFrame)]   // not inherited: the scheduler reads the attribute off the concrete type
[NodeFeature(GameplayFeatures.DeployServerId)]
public sealed class ServerPlayerSlotReaderSystem : PlayerSlotReaderSystem
{
    public ServerPlayerSlotReaderSystem(GameplayWire wire) : base(wire) { }
}
