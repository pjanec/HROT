using System;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    /// <summary>Discriminator for the setting payload union.</summary>
    public enum SettingType : byte { Bool = 0, Int32 = 1, Float32 = 2 }

    /// <summary>
    /// 8-byte tagged union that stores one gizmo setting value.
    /// The <see cref="Type"/> byte selects which payload field is active.
    /// All three payload fields share the same 4 bytes at offset 4.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct GizmoSettingValue : IEquatable<GizmoSettingValue>
    {
        [FieldOffset(0)] public SettingType Type;

        [FieldOffset(4)] public bool  BoolValue;
        [FieldOffset(4)] public int   IntValue;
        [FieldOffset(4)] public float FloatValue;

        public static GizmoSettingValue From(bool  v) => new() { Type = SettingType.Bool,    BoolValue  = v };
        public static GizmoSettingValue From(int   v) => new() { Type = SettingType.Int32,   IntValue   = v };
        public static GizmoSettingValue From(float v) => new() { Type = SettingType.Float32, FloatValue = v };

        /// <summary>
        /// Compares type tag and the 4-byte payload (via IntValue overlay, which covers
        /// all three overlapping fields at offset 4).
        /// </summary>
        public bool Equals(GizmoSettingValue other)
            => Type == other.Type && IntValue == other.IntValue;

        public override bool Equals(object? obj)
            => obj is GizmoSettingValue other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine((int)Type, IntValue);

        public static bool operator ==(GizmoSettingValue l, GizmoSettingValue r) => l.Equals(r);
        public static bool operator !=(GizmoSettingValue l, GizmoSettingValue r) => !l.Equals(r);
    }
}
