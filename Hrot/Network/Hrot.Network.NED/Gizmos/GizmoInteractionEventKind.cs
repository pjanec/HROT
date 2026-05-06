namespace Hrot.Network.NED.Gizmos
{
    public enum GizmoInteractionEventKind : byte
    {
        Started    = 0,
        DragUpdate = 1,
        Commit     = 2,
        Cancel     = 3,
    }
}
