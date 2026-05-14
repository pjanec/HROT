namespace Hrot.StrideMock;

/// <summary>
/// Abstract base that mirrors the Stride SyncScript API so that
/// <see cref="SyncFdpToStrideScript"/> can be ported to a real Stride project
/// by swapping only the fake entity/effect types.
/// </summary>
public abstract class FakeStrideScript
{
    /// <summary>Called once before the first <see cref="Update"/> call.</summary>
    public abstract void Start();

    /// <summary>Called every frame with the elapsed time in seconds.</summary>
    public abstract void Update(float deltaTime);
}
