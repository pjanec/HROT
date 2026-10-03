using System.Collections.Generic;

namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-2027</c> — the ONE table of type ids whose layout is known without a compilation</b>: the primitives, their C#
    /// aliases and the engine-math vectors, each with its CLR (size, alignment).
    ///
    /// <para>
    /// 🔴 It existed three times — <c>Hrot.AiEditor.Persistence.BTreeBlackboardPackHelper.KnownSizes</c> (the blackboard
    /// packer's), <c>Hrot.AiEditor.Generators.StructSizeResolver.KnownSizes</c> ("mirrors …") and the vector arm of the analyzers'
    /// <c>GetTypeSize</c> — and none carried an ALIGNMENT, so every reader guessed <c>min(size, 8)</c>: a 12-byte
    /// <c>Vector3</c> came out 8-aligned where the CLR aligns it to 4.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>Roslyn-free and dependency-free</b> — it is LINKED into the Roslyn-free <c>Hrot.AiEditor.Persistence</c> as well as
    /// into every analyzer and generator. <see cref="RoslynStructLayout"/> is its symbol-side companion.
    /// </para>
    /// </summary>
    internal static class KnownTypeLayouts
    {
        private static readonly Dictionary<string, (int Size, int Align)> Layouts =
            new Dictionary<string, (int Size, int Align)>(System.StringComparer.Ordinal)
            {
                { "System.Boolean", (1, 1) }, { "bool",   (1, 1) },
                { "System.Byte",    (1, 1) }, { "byte",   (1, 1) },
                { "System.SByte",   (1, 1) }, { "sbyte",  (1, 1) },
                { "System.Char",    (2, 2) }, { "char",   (2, 2) },
                { "System.Int16",   (2, 2) }, { "short",  (2, 2) },
                { "System.UInt16",  (2, 2) }, { "ushort", (2, 2) },
                { "System.Int32",   (4, 4) }, { "int",    (4, 4) },
                { "System.UInt32",  (4, 4) }, { "uint",   (4, 4) },
                { "System.Single",  (4, 4) }, { "float",  (4, 4) },
                { "System.Int64",   (8, 8) }, { "long",   (8, 8) },
                { "System.UInt64",  (8, 8) }, { "ulong",  (8, 8) },
                { "System.Double",  (8, 8) }, { "double", (8, 8) },
                // The engine-math vectors: float aggregates, so 4-aligned whatever their size.
                { "System.Numerics.Vector2",    (8, 4)  }, { "UnityEngine.Vector2",    (8, 4)  }, { "Vector2",    (8, 4)  },
                { "System.Numerics.Vector3",    (12, 4) }, { "UnityEngine.Vector3",    (12, 4) }, { "Vector3",    (12, 4) },
                { "System.Numerics.Vector4",    (16, 4) }, { "UnityEngine.Vector4",    (16, 4) }, { "Vector4",    (16, 4) },
                { "System.Numerics.Quaternion", (16, 4) }, { "UnityEngine.Quaternion", (16, 4) }, { "Quaternion", (16, 4) },
            };

        /// <summary>The (size, alignment) of a known type id — a CLR name, a C# alias or a short vector name.</summary>
        public static bool TryGet(string typeId, out int size, out int align)
        {
            if (typeId != null && Layouts.TryGetValue(typeId, out var layout))
            {
                size = layout.Size; align = layout.Align;
                return true;
            }
            size = 0; align = 1;
            return false;
        }

        /// <summary>The size of a known type id (the packer's question).</summary>
        public static bool TryGetSize(string typeId, out int size) => TryGet(typeId, out size, out _);
    }
}
