namespace GizmoMap.Network
{
    public enum GizmoInteractionEventKind : byte
    {
        Started    = 0,
        DragUpdate = 1,
        Commit     = 2,
        Cancel     = 3,
        MenuAction = 4,  // Emitted when the operator clicks a context menu item; ActionId carries the item id
    }
}
