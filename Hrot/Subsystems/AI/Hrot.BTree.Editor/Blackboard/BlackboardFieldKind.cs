namespace Hrot.BTree.Editor.Blackboard;

public enum BlackboardFieldKind
{
    Bool,
    Numeric,  // int, float, double, long, short, byte, uint, ulong, ushort, sbyte, decimal
    Vector,   // System.Numerics.Vector2, Vector3, Vector4
    Enum,
    Struct,   // user-defined struct (not in above categories)
    Other,    // class types, interfaces, pointers, etc.
}
