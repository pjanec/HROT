using Fdp.Kernel;
using System.Numerics;

namespace Fdp.Examples.BattleRoyale.Events;

[EventId(1002)]
public struct KillEvent
{
    public Entity Victim;
    public Entity Killer;
    public Vector3 Position;
    public uint Tick;
}
