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

    /// <summary>
    /// Ruling 14 — stage an edit together with the value the editor was SEEDED with, so the
    /// implementation can write only the bytes the designer actually changed.
    ///
    /// <para>
    /// 🔴 <b>Why the baseline is a parameter and not something the callee can find.</b> The staged
    /// value and the value in the world differ in TWO ways at drain time — the designer's edit and
    /// whatever the simulation changed during the paused tick — and nothing at the drain can tell
    /// those apart. Only the caller knows what the dialog opened on.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Default-implemented on purpose:</b> it forwards to the whole-component
    /// <see cref="StageMutation(Entity, Type, object)"/>, so an implementer that has no baseline —
    /// every test double, and any caller that genuinely replaces a component — keeps working
    /// unchanged and unsurgically.
    /// </para>
    /// </summary>
    void StageMutation(Entity entity, Type componentType, object componentValue, object? baseline)
        => StageMutation(entity, componentType, componentValue);

}
