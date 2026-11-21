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
    private readonly IGameplayWire _wire;
    private readonly Func<bool>? _when;
    [Fdp.Core.SessionState("the latest slot topic per station, as received (the cluster's join state, not the world)")]
    private readonly Dictionary<int, PlayerSlotTopic> _latest = new();
    [Fdp.Core.FrameScratch("the slot samples taken this frame: cleared before and after each read")]
    private readonly List<GameplaySample<PlayerSlotTopic>> _taken = new();

    /// <param name="when">⭐ Optional gate: the reader is created (and read) only while it says true. The Player's client pack passes "a deploy server exists", so a Player in a
    /// military-sim cluster with no server creates no gameplay reader; the server's subclass leaves it null (the feature attribute gates it).</param>
    public PlayerSlotReaderSystem(IGameplayWire wire, Func<bool>? when = null)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _when = when;
    }

    /// <summary>⭐ S2 — the latest slot sample of <paramref name="station"/> (the Player's flow reads its OWN slot through this — one reader of the topic, not two); false when it has none (never written, or disposed).</summary>
    public bool TryGetSlot(int station, out PlayerSlotTopic slot) => _latest.TryGetValue(station, out slot);

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        if (_when is not null && !_when()) return;
        var reader = _wire.SlotReader;
        if (reader is null) return;
        _taken.Clear();
        if (reader.TakeInto(_taken) == 0) return;
        for (int i = 0; i < _taken.Count; i++)
        {
            var s = _taken[i];
            if (s.Alive)
            {
                var slot = s.Data;
                _latest[slot.StationNodeId] = slot;
                // a Choosing or Dead slot holds a SIDE only (a dead man is no claim on anything)
                StationReservations.Of(repo).Set(slot.StationNodeId, (ForceId)slot.Side, slot.State is (byte)PlayerSlotState.Choosing or (byte)PlayerSlotState.Dead ? 0 : slot.UnitNetId);
            }
            else   // the slot ended: disposed by the server, or its writer is gone — the key rides in Data
            {
                _latest.Remove(s.Data.StationNodeId);
                StationReservations.Of(repo).Remove(s.Data.StationNodeId);
            }
        }
        _taken.Clear();
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
    public ServerPlayerSlotReaderSystem(IGameplayWire wire) : base(wire) { }
}
