using System.Numerics;
using Fdp.Core;

namespace Hrot.Blueprints.Tests;

/// <summary>Demo event types for Blueprint test scenarios.</summary>
[EventId(90010)]
internal struct HitEvent
{
    public Entity Target;
    public Entity Attacker;
    public float Damage;
    public Vector3 Direction;
}
