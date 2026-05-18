using System;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    [StructLayout(LayoutKind.Sequential, Size = 4)]
    public struct Rgba32 : IEquatable<Rgba32>
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Rgba32(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static readonly Rgba32 Red         = new Rgba32(255, 0,   0,   255);
        public static readonly Rgba32 Green       = new Rgba32(0,   255, 0,   255);
        public static readonly Rgba32 Yellow      = new Rgba32(255, 255, 0,   255);
        public static readonly Rgba32 White       = new Rgba32(255, 255, 255, 255);
        public static readonly Rgba32 Black       = new Rgba32(0,   0,   0,   255);
        public static readonly Rgba32 Transparent = new Rgba32(0,   0,   0,   0);

        public bool Equals(Rgba32 other) =>
            R == other.R && G == other.G && B == other.B && A == other.A;

        public override bool Equals(object? obj) =>
            obj is Rgba32 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(R, G, B, A);

        public static bool operator ==(Rgba32 left, Rgba32 right) => left.Equals(right);
        public static bool operator !=(Rgba32 left, Rgba32 right) => !left.Equals(right);
    }
}
