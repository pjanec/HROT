namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ CE-2380 part 1 (📄 docs/DESIGN_Remote_Edit_Outcome.md §2) — the state of a remote edit as its requester sees it. <c>None</c> = no outcome known (the default).
    /// <c>Pending</c> = the owner took it and a judge will decide later (the same request id then gets the final verdict).
    /// </summary>
    public enum EditStatus
    {
        None = 0,
        Applied = 1,
        Pending = 2,
        Refused = 3,
        /// <summary>⭐ Edit framework (📄 docs/UX/DESIGN_Edit_Framework.md §2.1): a precondition failed — "changed by someone else since you opened it" (F6).</summary>
        Conflict = 4,
        /// <summary>⭐ Edit framework §2.1: no answer within the kind's time-out — LOCAL only, never sent (the journal decides it).</summary>
        Lost = 5,
    }
}
