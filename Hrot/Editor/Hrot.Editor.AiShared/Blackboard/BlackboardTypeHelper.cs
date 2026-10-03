using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hrot.Editor.AiShared.Blackboard;

// Type name helpers shared between the emitter (for emit) and the window (for display).
public static class BlackboardTypeHelper
{
    // Returns the C# alias name for known primitives; otherwise Type.Name.
    // Examples: typeof(float) -> "float", typeof(int) -> "int", typeof(Vector3) -> "Vector3"
    public static string GetDisplayName(Type t)
    {
        if (BlackboardDtoEmitter.TypeAliases.TryGetValue(t, out string? alias))
            return alias;
        // FC-3a (Q#21-A1/B1): a recognized fixed-list wrapper displays as "List<T>[N]" in the
        // Variables panel (display-only v1) instead of its raw wrapper struct name.
        if (BlackboardFieldClassifier.TryGetFixedListShape(t, out var elem, out int capacity))
            return $"List<{GetDisplayName(elem)}>[{capacity}]";
        return t.Name;
    }

    // Maps display names to their CLR types for the known primitive and vector types.
    private static readonly Dictionary<string, Type> _primitiveTypes = new()
    {
        { "bool",       typeof(bool)       },
        { "byte",       typeof(byte)       },
        { "sbyte",      typeof(sbyte)      },
        { "short",      typeof(short)      },
        { "ushort",     typeof(ushort)     },
        { "int",        typeof(int)        },
        { "uint",       typeof(uint)       },
        { "long",       typeof(long)       },
        { "ulong",      typeof(ulong)      },
        { "float",      typeof(float)      },
        { "double",     typeof(double)     },
        { "Vector2",    typeof(Vector2)    },
        { "Vector3",    typeof(Vector3)    },
        { "Vector4",    typeof(Vector4)    },
        { "Quaternion", typeof(Quaternion) },
        // ⭐ DESIGN_Entity_Reference D6 — an authored reference to another entity (its network id): picked in the editor,
        // remapped at scenario load, resolved with EntityRef.Resolve. Unmanaged, 8 bytes, so a blackboard field can hold it.
        { "EntityRef",  typeof(Fdp.Toolkit.Replication.EntityRef) },
    };

    // Returns the CLR type for the given display name, or null if not a known type.
    public static Type? GetPrimitiveType(string name)
        => _primitiveTypes.TryGetValue(name, out Type? t) ? t : null;

    // Hardcoded default list of known type names for the Add Variable dropdown.
    public static readonly IReadOnlyList<string> DefaultKnownTypeNames = new string[]
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "Vector2", "Vector3", "Vector4", "Quaternion", "EntityRef",
    };

    /// <summary>
    /// ⭐⭐ <c>CE-439</c> — the ONE "type id → CLR type" rule for blackboard variables: a primitive alias, then
    /// <c>Type.GetType</c>, then every loaded assembly by full name (DTO structs — and a behaviour's generated Inputs struct —
    /// live in behaviour assemblies the editor did not reference). <c>typeof(object)</c> when nothing matches.
    /// ⚠ Was copied privately into both <c>BehaviorTreeAssetMapper</c> and <c>HsmAssetMapper</c>; the subtree compose step
    /// needed a third, so it moved here and both mappers call it.
    /// </summary>
    public static Type ResolveClrType(string typeId)
    {
        var primitive = GetPrimitiveType(typeId);
        if (primitive != null) return primitive;

        var t = Type.GetType(typeId);
        if (t != null) return t;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? byName;
            try { byName = asm.GetType(typeId, throwOnError: false, ignoreCase: false); }
            catch { byName = null; }   // dynamic/reflection-only assemblies may throw
            if (byName != null) return byName;
        }

        foreach (var name in DefaultKnownTypeNames)
        {
            var pt = GetPrimitiveType(name);
            if (pt != null && (pt.FullName == typeId || pt.Name == typeId)) return pt;
        }

        return typeof(object);
    }
}
