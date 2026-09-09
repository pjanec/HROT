using Fdp.Core;

namespace Hrot.Stride.Core;

/// <summary>
/// Trivial cross-TFM proof type: references both a Stride.Engine type (Entity)
/// and an FDP net8.0 type (EntityRepository), proving that a net8.0-windows →
/// net8.0 ProjectReference compiles cleanly.
///
/// This type has no runtime behavior beyond construction. It exists solely to
/// satisfy the compiler and the reference-guard test.
/// </summary>
public sealed class StrideCorePlaceholder
{
    /// <summary>A Stride ECS entity reference (Stride.Engine, net8.0-windows).</summary>
    public global::Stride.Engine.Entity? StrideEntity { get; set; }

    /// <summary>FDP entity repository (Fdp.Core, net8.0).</summary>
    public EntityRepository? FdpRepository { get; set; }
}
