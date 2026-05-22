namespace Hrot.Blueprints.Editor.GraphEditor;

public sealed class SelectionState
{
    public HashSet<Guid> SelectedNodes { get; } = new();
    public HashSet<Guid> SelectedLinks { get; } = new();

    public void ClearAll()
    {
        SelectedNodes.Clear();
        SelectedLinks.Clear();
    }

    public bool IsNodeSelected(Guid nodeId) => SelectedNodes.Contains(nodeId);
    public bool IsLinkSelected(Guid linkId) => SelectedLinks.Contains(linkId);

    public void SelectNode(Guid nodeId, bool addToSelection = false)
    {
        if (!addToSelection) ClearAll();
        SelectedNodes.Add(nodeId);
    }
}
