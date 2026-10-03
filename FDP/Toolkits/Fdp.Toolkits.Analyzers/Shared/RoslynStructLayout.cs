using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-2027</c> — the ONE compile-time struct-layout algorithm</b> (managed size, alignment and sequential field
    /// offset of a Roslyn type symbol). 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8d".
    ///
    /// <para>
    /// 🔴 It existed four times, held in step by "keep in sync" comments: <c>BehaviorParameterSizeAnalyzer</c>,
    /// <c>BTreeActionGenerator</c>, <c>HsmActionGenerator</c> (here) and <c>Hrot.AiEditor.Generators.StructSizeResolver</c>
    /// (linked into two generators). The documented reason — <i>"cannot be shared via a common helper assembly"</i>
    /// (<c>docs/projects/FDP/Toolkits/Fdp.Toolkits.Analyzers.md</c>) — is about ASSEMBLIES; a linked source file adds none.
    /// ⭐ This file is SHARED BY LINK (<c>&lt;Compile Include=… Link=…&gt;</c>), the same mechanism as its <c>Shared/</c>
    /// neighbours, into every analyzer and generator that sizes a struct.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>It must stay dependency-free:</b> Roslyn and the BCL, nothing else — every assembly that links it compiles it
    /// against its own references.
    /// </para>
    /// </summary>
    internal static class RoslynStructLayout
    {
        /// <summary>The sequential packing cap (the CLR default pack on 64-bit).</summary>
        public const int AlignmentCap = 8;

        /// <summary>The managed size of <paramref name="type"/>, or <c>-1</c> when it cannot be laid out (a class, an unknown
        /// pointer, an explicit field without <c>[FieldOffset]</c>).</summary>
        public static int TypeSize(ITypeSymbol type) => Of(type).Size;

        /// <summary>The CLR alignment of <paramref name="type"/> as a field (its largest primitive, capped at
        /// <see cref="AlignmentCap"/>). ⚠ NOT <c>min(size, 8)</c> — a 12-byte <c>Vector3</c> is 4-aligned.</summary>
        public static int TypeAlign(ITypeSymbol type) => Of(type).Align;

        /// <summary>The managed size of a struct (sequential or explicit layout), or <c>-1</c>.</summary>
        public static int StructSize(INamedTypeSymbol type) => OfStruct(type).Size;

        /// <summary>The CLR byte offset of <paramref name="fieldName"/> inside <paramref name="type"/>, or <c>null</c>.</summary>
        public static int? FieldOffset(INamedTypeSymbol type, string fieldName, out ITypeSymbol? fieldType)
        {
            fieldType = null;
            var fields = InstanceFields(type);
            if (IsExplicit(type))
            {
                var target = fields.FirstOrDefault(f => f.Name == fieldName);
                if (target == null) return null;
                int? fo = ExplicitOffset(target);
                if (fo == null) return null;
                fieldType = target.Type;
                return fo;
            }

            int offset = 0, pack = PackOf(type);
            foreach (var field in fields)
            {
                var (size, align) = OfField(field);
                if (size < 0) return null;
                offset = AlignUp(offset, System.Math.Min(align, pack));
                if (field.Name == fieldName) { fieldType = field.Type; return offset; }
                offset += size;
            }
            return null;
        }

        /// <summary>The size of a sequential struct whose fields' (size, alignment), in declaration order, are given — for a
        /// struct that does not exist yet (a sibling generator's), laid out from its schema.</summary>
        public static int SequentialSize(IReadOnlyList<(int Size, int Align)> fieldsInOrder)
        {
            if (fieldsInOrder.Count == 0) return 1;
            int offset = 0, maxAlign = 1;
            foreach (var (size, align) in fieldsInOrder)
            {
                int a = System.Math.Max(1, System.Math.Min(align, AlignmentCap));
                if (a > maxAlign) maxAlign = a;
                offset = AlignUp(offset, a) + size;
            }
            return AlignUp(offset, maxAlign);
        }

        public static int AlignUp(int v, int a) => a <= 1 ? v : (v + a - 1) / a * a;

        // ── the CLR rules ────────────────────────────────────────────────────────────────────────────────────────────────

        private static (int Size, int Align) Of(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:   return (1, 1);
                case SpecialType.System_Char:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:  return (2, 2);
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Single:  return (4, 4);
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr: return (8, 8);
            }
            if (type is not INamedTypeSymbol named) return (-1, 1);
            if (KnownTypeLayouts.TryGet(MetadataName(named), out int known, out int knownAlign)) return (known, knownAlign);
            if (type.TypeKind == TypeKind.Enum)
                return named.EnumUnderlyingType != null ? Of(named.EnumUnderlyingType) : (4, 4);
            if (type.TypeKind == TypeKind.Struct) return OfStruct(named);
            return (-1, 1);
        }

        /// <summary>A field's layout — a <c>fixed T X[N]</c> buffer is N elements of T, aligned as T.</summary>
        private static (int Size, int Align) OfField(IFieldSymbol field)
        {
            if (field.IsFixedSizeBuffer && field.Type is IPointerTypeSymbol ptr)
            {
                var (size, align) = Of(ptr.PointedAtType);
                return size < 0 ? (-1, 1) : (size * field.FixedSize, align);
            }
            return Of(field.Type);
        }

        private static (int Size, int Align) OfStruct(INamedTypeSymbol type)
        {
            var fields = InstanceFields(type);
            int pack = PackOf(type), declaredSize = DeclaredSize(type);
            int end = 0, maxAlign = 1;

            if (IsExplicit(type))
            {
                foreach (var field in fields)
                {
                    int? fo = ExplicitOffset(field);
                    if (fo == null) return (-1, 1);
                    var (fs, fa) = OfField(field);
                    if (fs < 0) return (-1, 1);
                    end = System.Math.Max(end, fo.Value + fs);
                    maxAlign = System.Math.Max(maxAlign, System.Math.Min(fa, pack));
                }
            }
            else
            {
                foreach (var field in fields)
                {
                    var (fs, fa) = OfField(field);
                    if (fs < 0) return (-1, 1);
                    int a = System.Math.Min(fa, pack);
                    if (a > maxAlign) maxAlign = a;
                    end = AlignUp(end, a) + fs;
                }
            }

            int size = System.Math.Max(System.Math.Max(AlignUp(end, maxAlign), declaredSize), 1);   // an empty struct is 1 byte
            return (size, maxAlign);
        }

        /// <summary><c>[StructLayout(…, Pack = n)]</c> caps field alignment; 0 / absent ⇒ the default cap.</summary>
        private static int PackOf(INamedTypeSymbol type)
        {
            int pack = NamedArg(type, "Pack");
            return pack > 0 ? System.Math.Min(pack, AlignmentCap) : AlignmentCap;
        }

        /// <summary><c>[StructLayout(…, Size = n)]</c> is a minimum size.</summary>
        private static int DeclaredSize(INamedTypeSymbol type) => NamedArg(type, "Size");

        private static int NamedArg(INamedTypeSymbol type, string name)
        {
            var attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "StructLayoutAttribute");
            if (attr == null) return 0;
            foreach (var kv in attr.NamedArguments)
                if (kv.Key == name && kv.Value.Value is int n) return n;
            return 0;
        }

        private static List<IFieldSymbol> InstanceFields(INamedTypeSymbol type)
            => type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).ToList();

        private static bool IsExplicit(INamedTypeSymbol type)
            => type.GetAttributes().Any(a => a.AttributeClass?.Name == "StructLayoutAttribute"
                                          && a.ConstructorArguments.Length > 0
                                          && a.ConstructorArguments[0].Value is int v && v == 2);   // LayoutKind.Explicit

        private static int? ExplicitOffset(IFieldSymbol field)
        {
            var fa = field.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "FieldOffsetAttribute");
            return fa == null || fa.ConstructorArguments.Length == 0 ? (int?)null : (int)fa.ConstructorArguments[0].Value!;
        }

        private static string MetadataName(INamedTypeSymbol named)
            => named.ContainingNamespace?.IsGlobalNamespace == false
                ? named.ContainingNamespace.ToDisplayString() + "." + named.MetadataName
                : named.MetadataName;
    }
}
