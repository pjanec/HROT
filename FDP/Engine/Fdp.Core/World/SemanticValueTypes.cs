using System;
using System.Numerics;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ S0 item 6 (<c>docs/DESIGN_Scenario_Rules.md</c> SR-141 / SR-142 / SR-143) — the ONE mapping from a SEMANTIC value type to the storage
    /// type a generic tool (an inspector widget, a JSON shape, a blackboard editor, a validator) already knows how to handle:
    /// <see cref="WorldPoint"/> → <see cref="Vector3"/>, <see cref="WorldRotation"/> → <see cref="Quaternion"/>, <see cref="Bearing"/> → <see cref="float"/>.
    /// Every site that used to switch on <c>typeof(Vector3)</c> / <c>typeof(Quaternion)</c> / <c>float</c> for a FIELD's type routes through
    /// here, so a <c>WorldPoint</c> field draws, edits, serialises and validates exactly as a <c>Vector3</c> did.
    /// ⚠ This is for the GENERIC widget. A tool that shows the TYPE (a schema <c>type: "WorldPoint"</c>, a <c>picker: "worldLocation"</c>) asks the
    /// real type — the whole value of the semantic types is that a tool CAN tell a place from a direction.
    /// </summary>
    public static class SemanticValueTypes
    {
        /// <summary><see cref="WorldPoint"/> → <see cref="Vector3"/>, <see cref="WorldRotation"/> → <see cref="Quaternion"/>,
        /// <see cref="Bearing"/> → <see cref="float"/>; any other type → itself.</summary>
        public static Type StorageTypeOf(Type t)
        {
            if (t == typeof(WorldPoint)) return typeof(Vector3);
            if (t == typeof(WorldRotation)) return typeof(Quaternion);
            if (t == typeof(Bearing)) return typeof(float);
            return t;
        }

        /// <summary><c>true</c> when <paramref name="t"/> is one of the semantic types (so <see cref="StorageTypeOf"/> changes it).</summary>
        public static bool IsSemantic(Type t) => t == typeof(WorldPoint) || t == typeof(WorldRotation) || t == typeof(Bearing);

        /// <summary>A boxed semantic value → its boxed storage value (<see cref="WorldPoint"/> → <see cref="Vector3"/> …); other values unchanged.</summary>
        public static object? ToStorage(object? value) => value switch
        {
            WorldPoint p => (Vector3)p,
            WorldRotation r => (Quaternion)r,
            Bearing b => b.Degrees,
            _ => value,
        };

        /// <summary>A boxed storage value → the boxed semantic type <paramref name="semantic"/> (<see cref="Vector3"/> → <see cref="WorldPoint"/> …);
        /// a value whose type already is <paramref name="semantic"/>, or any other type, is returned unchanged.</summary>
        public static object? FromStorage(object? value, Type semantic)
        {
            if (value is null) return null;
            if (semantic == typeof(WorldPoint) && value is Vector3 v) return (WorldPoint)v;
            if (semantic == typeof(WorldRotation) && value is Quaternion q) return (WorldRotation)q;
            if (semantic == typeof(Bearing) && value is float f) return (Bearing)f;
            return value;
        }
    }
}
