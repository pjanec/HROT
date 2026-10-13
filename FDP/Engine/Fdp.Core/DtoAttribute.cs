using System;
using System.Runtime.CompilerServices;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ <c>CE-3185</c> (P1, F1 / F9, R-2000 — docs/DESIGN_Orders_Params_Entity_Classes.md §13.7) — marks an AUTHORED DATA STRUCT: one
    /// registry (<c>ParamsDtoRegistry</c>) scans this attribute and every attribute derived from it, so a struct is a blackboard
    /// variable type, a params DTO, an order DTO or a unit memory by its attribute alone. Applied bare (<c>[Dto]</c>) it is a plain
    /// variable type (what <c>[BlackboardDtoStruct]</c> said before it retired).
    /// <para>⭐ It records its ORIGIN, a locator for "open source / open asset" — never an identity (the identity is the CLR type FQN):
    /// the compiler fills <see cref="SourceFile"/> / <see cref="SourceLine"/> at the use site (caller-info arguments on the
    /// constructor), and a generated DTO carries its asset's <see cref="AssetId"/>, which wins.</para>
    /// <para>⚠ A derived attribute must take the same two caller-info parameters LAST and pass them on, or the compiler fills them with
    /// the derived attribute's own file.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class DtoAttribute : Attribute
    {
        public DtoAttribute([CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
        {
            SourceFile = sourceFile ?? "";
            SourceLine = sourceLine;
        }

        /// <summary>The C# file that declares the DTO (absolute, as the compiler saw it), or empty.</summary>
        public string SourceFile { get; }

        /// <summary>The line of the attribute in <see cref="SourceFile"/>, or 0.</summary>
        public int SourceLine { get; }

        /// <summary>The generating asset's id (a GUID string) — set only by a generator; when set, the asset is the origin.</summary>
        public string? AssetId { get; set; }
    }

    /// <summary>
    /// ⭐ <c>CE-2136</c> / <c>CE-3185</c> — marks a PARAMS DTO: the public JSON contract a behaviour names (<c>[Behavior(Params = …)]</c> or
    /// an asset's <c>ParamsDtoTypeId</c>). Every params DTO carries it (R-2000) — hand-written, or emitted by the params DTO asset's
    /// generator with its <see cref="DtoAttribute.AssetId"/>. An <c>[Order]</c> DTO is a params DTO by that attribute and needs no second one.
    /// <para>⭐ Lives in <c>Fdp.Core</c> (moved from <c>Hrot.Core</c>) so a DTO in <c>Fdp.Toolkits</c> can carry it.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ParamsDtoAttribute : DtoAttribute
    {
        public ParamsDtoAttribute([CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
            : base(sourceFile, sourceLine) { }

        /// <summary>The DTO's default as one JSON object (only the fields that have one) — what a behaviour's
        /// <c>ParamsDefaultJson</c> can be seeded from. <c>default(T)</c> cannot carry it (R-237).</summary>
        public string? DefaultJson { get; set; }
    }

    /// <summary>
    /// Marks a struct as a UNIT MEMORY (Q87, R-237): one instance per unit, shared by every behaviour that touches it,
    /// created on first touch with <c>new T()</c> and living as long as the unit. Declare the defaults as field
    /// initializers AND a parameterless constructor (<c>public Foo() {}</c>) — C# applies initializers only through a
    /// constructor call (CS8983 without one). Reached at runtime through <c>Fdp.Toolkit.Behavior.UnitMemory</c>.
    /// <para>⭐ <c>CE-3185</c> — moved out of the vendored <c>Fbt.Kernel</c> (the kernel never read it) onto the <see cref="DtoAttribute"/> base.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class UnitMemoryAttribute : DtoAttribute
    {
        public UnitMemoryAttribute([CallerFilePath] string sourceFile = "", [CallerLineNumber] int sourceLine = 0)
            : base(sourceFile, sourceLine) { }
    }
}
