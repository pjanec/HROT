using System.Numerics;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ CE-1033 S1 — the ONE coordinate transform of the 3-D map (<c>docs/DESIGN_Map_3D_Mode.md</c> M9): HROT is Z-up
    /// (x east, y north, z up), Raylib is Y-up. <c>(x, y, z) → (x, z, −y)</c> is a proper rotation (det +1, −90° about X), so
    /// winding, cross products and handedness survive it; a quaternion's vector part goes through the same matrix.
    /// </summary>
    public static class HrotToRaylib
    {
        /// <summary>A HROT point or direction in Raylib coordinates.</summary>
        public static Vector3 Position(Vector3 p) => new(p.X, p.Z, -p.Y);

        /// <summary>A Raylib point or direction back in HROT coordinates.</summary>
        public static Vector3 ToHrot(Vector3 r) => new(r.X, -r.Z, r.Y);

        /// <summary>A HROT rotation in Raylib coordinates (the vector part rotated like a point).</summary>
        public static Quaternion Rotation(Quaternion q) => new(q.X, q.Z, -q.Y, q.W);

        /// <summary>
        /// The Raylib model matrix of a part: scale <paramref name="size"/> (HROT x/y/z extents), then rotate by
        /// <paramref name="rotation"/>, then move to <paramref name="position"/> — all given in HROT terms. ⚠ Raylib reads a matrix
        /// transposed relative to <see cref="Matrix4x4"/>'s row-vector convention, hence the final transpose.
        /// </summary>
        public static Matrix4x4 Model(Vector3 position, Quaternion rotation, Vector3 size)
            => ModelFromHrot(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position));

        /// <summary>
        /// A transform written entirely in HROT terms (row-vector <see cref="Matrix4x4"/>, applied to a mesh whose local axes are
        /// read as HROT axes) as the matrix Raylib's <c>DrawMesh</c> takes: change basis in, apply, change basis out, transpose.
        /// </summary>
        public static Matrix4x4 ModelFromHrot(Matrix4x4 hrot) => Matrix4x4.Transpose(ToHrotBasis * hrot * ToRaylibBasis);

        /// <summary>Row-vector basis change HROT → Raylib: <c>v * ToRaylibBasis = (x, z, −y)</c>.</summary>
        public static readonly Matrix4x4 ToRaylibBasis = new(
            1, 0, 0, 0,
            0, 0, -1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);

        /// <summary>Its inverse (the transpose — a rotation): <c>v * ToHrotBasis = (x, −z, y)</c>.</summary>
        public static readonly Matrix4x4 ToHrotBasis = Matrix4x4.Transpose(ToRaylibBasis);
    }
}
