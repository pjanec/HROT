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
            // HillAttackSharedState (tree-integration shared commander struct, architect Q9): int + 3x
            // ushort + MemberSlotList (96, 8-aligned) + long + int + float + byte -> 8-aligned = 136.
            ["Hrot.AI.Behaviors.Brains.HillAttackSharedState"] = Unmanaged("Hrot.AI.Behaviors.Brains.HillAttackSharedState", 136),

            // Common aliases used in test assets -- and, since BP-87, the exact strings the editor's
            // type picker writes into an asset. Every alias here maps to the CANONICAL FullName, so
            // the coercion table below (keyed on FullName) sees `uint` and `System.UInt32` alike.
            ["bool"]   = Unmanaged("System.Boolean", 1),
            ["byte"]   = Unmanaged("System.Byte",    1),
            ["sbyte"]  = Unmanaged("System.SByte",   1),
            ["short"]  = Unmanaged("System.Int16",   2),
            ["ushort"] = Unmanaged("System.UInt16",  2),
            ["int"]    = Unmanaged("System.Int32",   4),
            ["uint"]   = Unmanaged("System.UInt32",  4),
            ["long"]   = Unmanaged("System.Int64",   8),
            ["ulong"]  = Unmanaged("System.UInt64",  8),
            ["float"]  = Unmanaged("System.Single",  4),
            ["double"] = Unmanaged("System.Double",  8),

            // BP-87: the vector types were registered under their FQN only, so the bare name the
            // picker offered ("Vector3") failed to resolve -- BP1500 on an asset the editor itself
            // produced.
            ["Vector2"]    = Unmanaged("System.Numerics.Vector2",    8),
            ["Vector3"]    = Unmanaged("System.Numerics.Vector3",    12),
            ["Vector4"]    = Unmanaged("System.Numerics.Vector4",    16),
            ["Quaternion"] = Unmanaged("System.Numerics.Quaternion", 16),

            // BP-87: the blittable fixed-length strings the user asked to be able to pick. Bare
            // aliases for symmetry with the vectors above.
            ["FixedString32"] = Unmanaged("Fdp.Core.FixedString32", 32),
            ["FixedString64"] = Unmanaged("Fdp.Core.FixedString64", 64),
        };

    /// <summary>
    /// BP-87: the type IDs a blueprint editor may offer in a parameter/variable type picker.
    ///
    /// <para>
    /// ⚠ <b>Every entry MUST be a key of <see cref="TypeTable"/> above</b>, and any numeric pair the
    /// designer can plausibly wire together must have a rung in <see cref="CoercionTable"/> below.
    /// Both are locked by <c>BP87_TypePickerTests</c>. This list lives here, beside the two tables it
    /// depends on, precisely so the drift that caused BP-87 is visible in one file: the picker used to
    /// be a hand-maintained array in <c>Hrot.Editor.AiShared</c> that offered <b>eight types the
    /// compiler could not resolve</b> (<c>sbyte ushort uint ulong</c> unregistered under any name;
    /// <c>Vector2/3/4 Quaternion</c> registered under their FQN only).
    /// </para>
    ///
    /// <para>
    /// ⚠ Deliberately <b>not</b> just <c>TypeTable.Keys</c>: that table also carries curated project
    /// structs (<c>MemberSlotList</c>, <c>WaveState</c>, …), the managed <c>System.String</c>/
    /// <c>System.Object</c>, and every FQN spelling of the aliases — none of which belong in a picker.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> EditorOfferableTypeIds = new[]
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double",
        "Vector2", "Vector3", "Vector4", "Quaternion",
        "FixedString32", "FixedString64",
    };

    // Coercion table: (fromFullName, toFullName) --> C# expression template
    //
    // BP-87 item 4: this is C#'s own implicit-numeric-conversion table (minus `decimal`, which the
    // registry does not carry), and nothing more. Widening only -- there is deliberately no
    // Int32->UInt32 or Int64->Int32 rung, because C# itself demands an explicit cast for those and a
    // silent lossy coercion in a visual graph is a wrong-VALUES bug the designer cannot see.
    //
    // ⚠ Why the unsigned rungs are not optional. Before BP-87 this table had exactly eight entries
    // and EVERY ONE was signed: registering `uint`/`ushort` as pickable types without them would have
    // produced types that RESOLVE but cannot be WIRED -- a worse failure than the BP1500 they replace,
    // because it surfaces later and reads as an editor glitch rather than a type error. The user's
    // condition for keeping the unsigned types was explicit: "as long as it can be seamlessly
    // converted to ints (wiring possible between uint <-> ushort <-> int pins)".
    private static readonly IReadOnlyDictionary<(string From, string To), string> CoercionTable =
        new Dictionary<(string, string), string>
        {
            // sbyte -> short, int, long, float, double
            { ("System.SByte",  "System.Int16"),  "(short)$expr"  },
            { ("System.SByte",  "System.Int32"),  "(int)$expr"    },
            { ("System.SByte",  "System.Int64"),  "(long)$expr"   },
            { ("System.SByte",  "System.Single"), "(float)$expr"  },
            { ("System.SByte",  "System.Double"), "(double)$expr" },

            // byte -> short, ushort, int, uint, long, ulong, float, double
            { ("System.Byte",   "System.Int16"),  "(short)$expr"  },
            { ("System.Byte",   "System.UInt16"), "(ushort)$expr" },
            { ("System.Byte",   "System.Int32"),  "(int)$expr"    },
            { ("System.Byte",   "System.UInt32"), "(uint)$expr"   },
            { ("System.Byte",   "System.Int64"),  "(long)$expr"   },
            { ("System.Byte",   "System.UInt64"), "(ulong)$expr"  },
            { ("System.Byte",   "System.Single"), "(float)$expr"  },
            { ("System.Byte",   "System.Double"), "(double)$expr" },

            // short -> int, long, float, double
            { ("System.Int16",  "System.Int32"),  "(int)$expr"    },
            { ("System.Int16",  "System.Int64"),  "(long)$expr"   },
            { ("System.Int16",  "System.Single"), "(float)$expr"  },
            { ("System.Int16",  "System.Double"), "(double)$expr" },

            // ushort -> int, uint, long, ulong, float, double
            { ("System.UInt16", "System.Int32"),  "(int)$expr"    },
            { ("System.UInt16", "System.UInt32"), "(uint)$expr"   },
            { ("System.UInt16", "System.Int64"),  "(long)$expr"   },
            { ("System.UInt16", "System.UInt64"), "(ulong)$expr"  },
            { ("System.UInt16", "System.Single"), "(float)$expr"  },
            { ("System.UInt16", "System.Double"), "(double)$expr" },

            // int -> long, float, double
            { ("System.Int32",  "System.Int64"),  "(long)$expr"   },
            { ("System.Int32",  "System.Single"), "(float)$expr"  },
            { ("System.Int32",  "System.Double"), "(double)$expr" },

            // uint -> long, ulong, float, double
            { ("System.UInt32", "System.Int64"),  "(long)$expr"   },
            { ("System.UInt32", "System.UInt64"), "(ulong)$expr"  },
            { ("System.UInt32", "System.Single"), "(float)$expr"  },
            { ("System.UInt32", "System.Double"), "(double)$expr" },

            // long -> float, double  (implicit in C# despite the precision loss)
            { ("System.Int64",  "System.Single"), "(float)$expr"  },
            { ("System.Int64",  "System.Double"), "(double)$expr" },

            // ulong -> float, double  (likewise)
            { ("System.UInt64", "System.Single"), "(float)$expr"  },
            { ("System.UInt64", "System.Double"), "(double)$expr" },

            // float -> double
            { ("System.Single", "System.Double"), "(double)$expr" },
        };

    public bool TryResolve(BlueprintTypeRef typeRef, out IrTypeRef irType)
    {
        // FC-2/LV-1 (Q#19-B): fixed-capacity list variable. Capacity -- not IsArray -- is the list
        // discriminator (review F7). The element must itself resolve UNMANAGED (a managed element
        // fails resolve -> the standard BP1500 unresolvable-type path; Q#19 settled: no managed
        // elements, ever) and must not itself be a list (nested lists forbidden -- open point #5).
        // The resolved type is the per-class generated `__List_{Elem}_{N}` wrapper: genuinely
        // unmanaged with a REAL computed size (so it passes BP1503 and participates in the tier
        // budget), but SizeReliable=false (review F3: the SizeBytes-keyed alignment heuristic
        // over-pads composite types, so state layout must use the runtime Marshal.OffsetOf path --
        // CSharpEmitter's layoutFromRuntime fallback).
        if (typeRef.Capacity > 0)
        {
            if (typeRef.IsArray) { irType = null!; return false; }   // list-of-array is not a shape
            var elementRef = new BlueprintTypeRef { TypeId = typeRef.TypeId };
            if (!TryResolve(elementRef, out var elem) || !elem.IsUnmanaged || elem.Capacity > 0
                || elem.SizeBytes <= 0)
            {
                irType = null!;
                return false;
            }

            int n         = typeRef.Capacity;
            int elemAlign = elem.SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 };
            int header    = AlignUp(4, elemAlign);                    // int Count + pad to element
            int size      = AlignUp(header + n * elem.SizeBytes, Math.Max(4, elemAlign));

            irType = new IrTypeRef
            {
                FullName      = $"__List_{Sanitize(elem.FullName)}_{n}",
                IsUnmanaged   = true,
                SizeBytes     = size,
                SizeReliable  = false,   // F3: runtime Marshal.OffsetOf layout, never baked offsets
                Capacity      = n,
                InitialLength = typeRef.InitialLength,
                ElementType   = elem,
            };
            return true;
        }

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

    /// <summary>FC-2/LV-1: element FQN → wrapper-name segment ('.'/'+' → '_', e.g. "System.Int32" → "System_Int32").</summary>
    private static string Sanitize(string fullName)
        => fullName.Replace('.', '_').Replace('+', '_');

    private static int AlignUp(int offset, int align)
        => (offset + align - 1) & ~(align - 1);
}
