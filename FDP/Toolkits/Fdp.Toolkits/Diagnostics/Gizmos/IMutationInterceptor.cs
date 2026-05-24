using System;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos;

/// <summary>
/// Allows an external pause manager to intercept component edits that would
/// otherwise be applied immediately.
/// When <see cref="IsPaused"/> is true, <see cref="StageMutation"/> is called
/// instead of the direct repo write.
/// </summary>
public interface IMutationInterceptor
{
    /// <summary>True while the simulation is paused by the breakpoint manager.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// Stages a component mutation to be applied at the next step/continue boundary.
    /// </summary>
    void StageMutation(Entity entity, Type componentType, object componentValue);
}
