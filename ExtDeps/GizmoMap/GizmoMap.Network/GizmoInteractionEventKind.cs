namespace GizmoMap.Network
{
    public enum GizmoInteractionEventKind : byte
    {
        Started    = 0,
        DragUpdate = 1,
        Commit     = 2,
        Cancel     = 3,
    }
}
