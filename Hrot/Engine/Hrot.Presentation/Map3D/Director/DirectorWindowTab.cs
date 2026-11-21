using System.Collections.Generic;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>What a Director window tab may use: the runner, the takes, and the ONE way to start a run (the window shows its refusal).</summary>
    public interface IDirectorTabContext
    {
        DirectorRunner Runner { get; }
        TakeStore Store { get; }
        /// <summary>Plays <paramref name="request"/>; the problems that refuse it are shown in the window.</summary>
        void Start(PlayRequest request);
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P1 (docs/DESIGN_Director_Timeline_Editor.md §11j) — a tab a host contributes to the Director window after its film
    /// tabs (the editor's Verify tab). The same shape as <see cref="IRunExtension"/>: the window knows no kind of run; a tab that owns a run's
    /// result also words its report row.
    /// </summary>
    public interface IDirectorWindowTab
    {
        string Name { get; }

        /// <summary>Draws the tab's body (the window draws the status table below it).</summary>
        void Draw(IDirectorTabContext context);

        /// <summary>The report line of an item whose result this tab owns (null: not this tab's).</summary>
        string? ReportLine(ItemReport report) => null;

        /// <summary>The tooltip lines of such a row (null: none).</summary>
        IEnumerable<string>? ReportDetail(ItemReport report) => null;

        /// <summary>The run a row's button repeats for such an item (null: the window's film replay).</summary>
        PlayRequest? Again(ItemReport report) => null;
    }
}
