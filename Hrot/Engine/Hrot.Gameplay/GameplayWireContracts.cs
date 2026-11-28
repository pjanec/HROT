namespace Hrot.Gameplay;

/// <summary>
/// ⭐⭐ <c>CE-11027</c> S12-2 (<c>docs/DESIGN_Player_Join.md</c> §12.2, Q114 C) — one received gameplay sample. <see cref="Alive"/> false = the instance ENDED (disposed, or its writer is gone): <see cref="Data"/> then carries the KEY
/// (the other fields are default), exactly what the DDS end decodes with <c>DdsTypeSupport.FromNative</c>.
/// </summary>
public readonly struct GameplaySample<T>
{
    public readonly T Data;
    public readonly bool Alive;
    public GameplaySample(T data, bool alive) { Data = data; Alive = alive; }
}

/// <summary>⭐ S12-2 — the reading end of one gameplay topic. <see cref="TakeInto"/> APPENDS everything pending to <paramref name="into"/> (the caller's reused buffer) and returns how many; nothing pending ⇒ 0 and no allocation (it runs every frame, R-2084).</summary>
public interface IGameplayIn<T>
{
    int TakeInto(List<GameplaySample<T>> into);
}

/// <summary>⭐ S12-2 — the writing end of one gameplay topic. <see cref="DisposeInstance"/> ends the instance named by the KEY fields of <paramref name="key"/>.</summary>
public interface IGameplayOut<T>
{
    void Write(in T value);
    void DisposeInstance(in T key);
}

/// <summary>
/// ⭐⭐ <c>CE-11027</c> S12-2 — the gameplay wire of ONE node, as the deploy server, the deploy flow and the slot reader see it: ONE interface, TWO transports (the cluster's DDS end <see cref="GameplayWire"/>, the editor's in-process
/// <see cref="LocalGameplayHub"/> ends), so no second implementation of any of the three. A member is null when the transport has no such end for this node (DDS: no participant; a hub end: the other role's members).
/// </summary>
public interface IGameplayWire
{
    // the SERVER's ends
    IGameplayIn<DeployRequestTopic>? Requests { get; }
    IGameplayOut<DeployStatusTopic>? Status { get; }
    IGameplayOut<DeployOfferTopic>? Offers { get; }
    IGameplayOut<PlayerSlotTopic>? SlotWriter { get; }
    // the PLAYER's ends (and the slot reader, which the server composes too)
    IGameplayOut<DeployRequestTopic>? RequestWriter { get; }
    IGameplayIn<DeployStatusTopic>? StatusReader { get; }
    IGameplayIn<DeployOfferTopic>? OfferReader { get; }
    IGameplayIn<PlayerSlotTopic>? SlotReader { get; }
}
