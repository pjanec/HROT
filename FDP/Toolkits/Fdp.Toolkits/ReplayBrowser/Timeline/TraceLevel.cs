using System;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// The trace detail a lane shows. ⭐ <c>R-4078</c> (docs/DESIGN_Director_Timeline_Editor.md §9c) — moved here from
    /// <c>Hrot.Editor.AiShared</c> with the lane seam, so the editor's trace lanes and the recording's lanes are one kind of thing.
    /// </summary>
    [Flags]
    public enum TraceLevel
    {
        None      = 0,
        Lifecycle = 1 << 0,
        Decisions = 1 << 1,
        Values    = 1 << 2,
        Async     = 1 << 3,
        Errors    = 1 << 4,
        All       = Lifecycle | Decisions | Values | Async | Errors,
    }
}
