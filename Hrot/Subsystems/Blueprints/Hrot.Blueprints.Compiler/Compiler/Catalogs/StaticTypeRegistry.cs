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

            // Managed reference types (not allowed in unmanaged state structs)
            ["System.String"] = new IrTypeRef { FullName = "System.String", IsUnmanaged = false, SizeBytes = 0 },
            ["System.Object"] = new IrTypeRef { FullName = "System.Object", IsUnmanaged = false, SizeBytes = 0 },

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

            // EQS sensor handle -- wraps Entity (8 bytes), unmanaged value type
            ["FDP.Eqs.EqsSensorHandle"] = new IrTypeRef
            {
                FullName    = "FDP.Eqs.EqsSensorHandle",
                IsUnmanaged = true,
                SizeBytes   = 8,
            },

            // Fdp.Core fixed-length string value types (unmanaged, blittable; preferred over System.String in state)
            ["Fdp.Core.FixedString32"] = Unmanaged("Fdp.Core.FixedString32", 32),
            ["Fdp.Core.FixedString64"] = Unmanaged("Fdp.Core.FixedString64", 64),

            // Curated blittable structs used as Blueprint WorkingState vars (reflection-free compiler ->
            // FQN + size declared here, exactly like Entity/EqsSensorHandle/FixedString above; the
            // `global::` acceptance path below deliberately does NOT cover these because it can only guess
            // a 4-byte enum-int32 size). MemberSlotList (Hill-attack wave core, architect Q#8) is the SoA
            // runner tracker: int Count (4) + 4 pad + long[8] (64) + byte[8]x3 (24) = 96 (Marshal.SizeOf).
            // NOTE: for an AiPrimitive WorkingState var the slot is sized at RUNTIME (Marshal.SizeOf<WorkingState>()),
            // so SizeBytes here is cosmetic for that path; it is declared correctly anyway for the offset/
            // debug-map bookkeeping that does read it. (A general curated-struct registration mechanism --
            // vs. hardcoding each here -- is future work if the curated-struct set grows.)
            ["Hrot.AI.Behaviors.Brains.MemberSlotList"] = Unmanaged("Hrot.AI.Behaviors.Brains.MemberSlotList", 96),
            // WaveState bundles MemberSlotList (96) + 2x ushort (4) -> 8-aligned (contains long) = 104.
            // Same cosmetic-size caveat as MemberSlotList (AiPrimitive WorkingState sized at runtime).
            ["Hrot.AI.Behaviors.Brains.WaveState"] = Unmanaged("Hrot.AI.Behaviors.Brains.WaveState", 104),

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

        if (TypeTable.TryGetValue(typeRef.TypeId, out irType!))
            return true;

        // AN2: Enum / project-type acceptance.
        // The editor persists enum TypeIds using the "global::" prefix (e.g. "global::Ns.MyEnum").
        // The reflection-less compiler cannot verify membership or size, so we accept any
        // "global::" TypeId as an unmanaged value type with the default enum underlying size of 4
        // bytes (System.Int32 backing -- the overwhelmingly common case).  The generator emits a
        // direct cast "(global::FQN)N" whose correctness is validated by the downstream C# compiler.
        // This is the "trust the JSON FQN + emit a cast" strategy documented in ENUM-DESIGN.md §RESOLVED.
        //
        // CONTRACT (AN2): the ASSET-level BlueprintTypeRef.TypeId for an enum carries the explicit
        // "global::" sentinel (= "global::" + FQN), per ENUM-DESIGN.md §RESOLVED / architect Q2.
        // The compiler-internal IrTypeRef.FullName is the UNPREFIXED FQN ("Ns.MyEnum"), consistent
        // with every other IrTypeRef.FullName (e.g. "System.Single", "Fdp.Core.FixedString32").
        // StatementEmitter.TypeRefToCSharp re-adds the "global::" exactly once on emit. Stripping the
        // prefix here is REQUIRED -- keeping it would emit "global::global::Ns.MyEnum" (CS0234).
        if (!string.IsNullOrEmpty(typeRef.TypeId) &&
            typeRef.TypeId.StartsWith("global::", StringComparison.Ordinal))
        {
            irType = new IrTypeRef
            {
                FullName    = typeRef.TypeId.Substring("global::".Length),
                IsUnmanaged = true,
                SizeBytes   = 4,   // default: System.Int32 underlying type
            };
            return true;
        }

        irType = null!;
        return false;
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
