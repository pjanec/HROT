using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Default type registry with built-in FDP + .NET primitive types.
/// Coercion table follows Compiler DD §7.3 (Slice 1 conservative set).
/// </summary>
public sealed class StaticTypeRegistry : ITypeRegistry
{
    public static readonly StaticTypeRegistry Instance = new();

    // -----------------------------------------------------------------------
    // Type table: TypeId string --> IrTypeRef
    // -----------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, IrTypeRef> TypeTable =
        new Dictionary<string, IrTypeRef>(StringComparer.OrdinalIgnoreCase)
        {
            // C# primitives
            ["System.Boolean"]  = Unmanaged("System.Boolean",  1),
            ["System.Byte"]     = Unmanaged("System.Byte",     1),
            ["System.SByte"]    = Unmanaged("System.SByte",    1),
            ["System.Int16"]    = Unmanaged("System.Int16",    2),
            ["System.UInt16"]   = Unmanaged("System.UInt16",   2),
            ["System.Int32"]    = Unmanaged("System.Int32",    4),
            ["System.UInt32"]   = Unmanaged("System.UInt32",   4),
            ["System.Int64"]    = Unmanaged("System.Int64",    8),
            ["System.UInt64"]   = Unmanaged("System.UInt64",   8),
            ["System.Single"]   = Unmanaged("System.Single",   4),
            ["System.Double"]   = Unmanaged("System.Double",   8),

            // Numeric vector types
            ["System.Numerics.Vector2"]     = Unmanaged("System.Numerics.Vector2",    8),
            ["System.Numerics.Vector3"]     = Unmanaged("System.Numerics.Vector3",    12),
            ["System.Numerics.Vector4"]     = Unmanaged("System.Numerics.Vector4",    16),
            ["System.Numerics.Quaternion"]  = Unmanaged("System.Numerics.Quaternion", 16),

            // FDP entity handle
            ["Fdp.Core.Entity"] = new IrTypeRef
            {
                FullName = "Fdp.Core.Entity",
                IsUnmanaged = true,
                SizeBytes = 8,
                IsEntityHandle = true,
            },

            // Common aliases used in test assets
            ["bool"]   = Unmanaged("System.Boolean", 1),
            ["byte"]   = Unmanaged("System.Byte",    1),
            ["short"]  = Unmanaged("System.Int16",   2),
            ["int"]    = Unmanaged("System.Int32",   4),
            ["long"]   = Unmanaged("System.Int64",   8),
            ["float"]  = Unmanaged("System.Single",  4),
            ["double"] = Unmanaged("System.Double",  8),
        };

    // Coercion table: (fromFullName, toFullName) --> C# expression template
    private static readonly IReadOnlyDictionary<(string From, string To), string> CoercionTable =
        new Dictionary<(string, string), string>
        {
            { ("System.Byte",   "System.Int32"),  "(int)$expr"    },
            { ("System.Byte",   "System.Single"), "(float)$expr"  },
            { ("System.Int16",  "System.Int32"),  "(int)$expr"    },
            { ("System.Int16",  "System.Single"), "(float)$expr"  },
            { ("System.Int32",  "System.Int64"),  "(long)$expr"   },
            { ("System.Int32",  "System.Single"), "(float)$expr"  },
            { ("System.Int32",  "System.Double"), "(double)$expr" },
            { ("System.Single", "System.Double"), "(double)$expr" },
        };

    public bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType)
    {
        if (typeRef.IsArray)
        {
            // Resolve element type first.
            if (!TryResolve(new BlueprintTypeRef { TypeId = typeRef.TypeId }, out var elementType))
            {
                irType = null!;
                return false;
            }
            irType = new IrTypeRef
            {
                FullName    = elementType.FullName + "[]",
                IsArray     = true,
                ElementType = elementType,
                IsUnmanaged = false,
                SizeBytes   = 0,
            };
            return true;
        }

        return TypeTable.TryGetValue(typeRef.TypeId, out irType!);
    }

    public bool TryGetCoercion(IrTypeRef from, IrTypeRef to, out string coercionExpression)
    {
        if (CoercionTable.TryGetValue((from.FullName, to.FullName), out var expr))
        {
            coercionExpression = expr;
            return true;
        }
        coercionExpression = "";
        return false;
    }

    private static IrTypeRef Unmanaged(string fullName, int sizeBytes) => new IrTypeRef
    {
        FullName    = fullName,
        IsUnmanaged = true,
        SizeBytes   = sizeBytes,
    };
}
