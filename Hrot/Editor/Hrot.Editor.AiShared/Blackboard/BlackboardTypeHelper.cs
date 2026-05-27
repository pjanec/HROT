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
        => BlackboardDtoEmitter.TypeAliases.TryGetValue(t, out string? alias)
            ? alias
            : t.Name;

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
    };

    // Returns the CLR type for the given display name, or null if not a known type.
    public static Type? GetPrimitiveType(string name)
        => _primitiveTypes.TryGetValue(name, out Type? t) ? t : null;

    // Hardcoded default list of known type names for the Add Variable dropdown.
    public static readonly IReadOnlyList<string> DefaultKnownTypeNames = new string[]
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "Vector2", "Vector3", "Vector4", "Quaternion",
    };
}
