namespace GizmoMap.Network
{
    public enum GizmoInteractionEventKind : byte
    {
        Started    = 0,
        DragUpdate = 1,
        Commit     = 2,
        Cancel     = 3,
        MenuAction = 4,  // Emitted when the operator clicks a context menu item; ActionId carries the item id
        RawInput   = 5,  // Raw HW event delivered while exclusive InputCaptureBinding is held
                         // ActionId: (int)MapMouseButton or (int)MapKeyboardKey
                         // stateFlags: bit7=1 mouse/0 keyboard; bit0=1 pressed/0 released
    }
}
