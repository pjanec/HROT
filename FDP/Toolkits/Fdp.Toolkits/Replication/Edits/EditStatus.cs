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
    }
}
