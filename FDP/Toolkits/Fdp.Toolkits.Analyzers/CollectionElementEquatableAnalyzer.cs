using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// FCOL005 (Fixed Collections, Q#21 wrap-up) — a blueprint-searchable collection whose
    /// element is a user STRUCT should implement <c>IEquatable&lt;T&gt;</c>.
    ///
    /// <para>
    /// The blueprint Contains/Find nodes compile to a search loop comparing elements via
    /// <c>EqualityComparer&lt;TElem&gt;.Default.Equals(...)</c>. For a struct that implements
    /// <c>IEquatable&lt;T&gt;</c> the comparer devirtualizes — allocation-free. For one that
    /// does NOT, the comparer falls back to <c>ObjectEqualityComparer&lt;T&gt;</c>, which calls
    /// <c>x.Equals((object)y)</c> and BOXES the argument on every comparison — silent heap
    /// allocations on the per-entity Tick hot path. The results stay correct; only the
    /// allocation contract breaks, which is why this is a warning, not an error.
    /// </para>
    ///
    /// Fires on both element-type declaration sites:
    /// <list type="bullet">
    ///   <item><c>[BlueprintCollectionItem]</c> read accessors (hand-written ops classes) —
    ///   the element is the accessor's return type;</item>
    ///   <item><c>[BlueprintCollectionField]</c> fields (generator inputs) — the element is
    ///   the <c>[InlineArray]</c> buffer's backing-field type.</item>
    /// </list>
    /// Primitives, enums, and structs that already implement the interface (e.g.
    /// <c>System.Numerics</c> vectors) never warn.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CollectionElementEquatableAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor FCOL005 = new(
            "FCOL005", "Collection element struct should implement IEquatable<T>",
            "Collection element type '{0}' is a struct without IEquatable<{0}> -- blueprint " +
            "Contains/Find will box it on every comparison (EqualityComparer<T>.Default falls " +
            "back to Object.Equals); implement IEquatable<{0}> for allocation-free search",
            "FixedCollections", DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(FCOL005);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
            context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            if (!method.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "BlueprintCollectionItemAttribute"))
                return;

            CheckElement(context, method.ReturnType, method);
        }

        private static void AnalyzeField(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            if (!field.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "BlueprintCollectionFieldAttribute"))
                return;

            // Element = the [InlineArray] buffer's single backing field's type. A malformed
            // buffer is FCOL002/003's job — stay silent here.
            if (field.Type is not INamedTypeSymbol bufType) return;
            if (!bufType.GetAttributes().Any(a => a.AttributeClass?.Name == "InlineArrayAttribute"))
                return;
            var backing = bufType.GetMembers().OfType<IFieldSymbol>()
                .Where(f => !f.IsStatic).ToList();
            if (backing.Count != 1) return;

            CheckElement(context, backing[0].Type, field);
        }

        private static void CheckElement(SymbolAnalysisContext context, ITypeSymbol elem, ISymbol site)
        {
            // Only user structs: primitives/framework specials and enums compare cleanly.
            if (elem.TypeKind != TypeKind.Struct) return;
            if (elem.SpecialType != SpecialType.None) return;
            if (elem is INamedTypeSymbol { EnumUnderlyingType: not null }) return;

            bool implementsEquatable = elem.AllInterfaces.Any(i =>
                i.Name == "IEquatable"
                && i.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                && i.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], elem));
            if (implementsEquatable) return;

            context.ReportDiagnostic(Diagnostic.Create(
                FCOL005,
                site.Locations.FirstOrDefault() ?? Location.None,
                elem.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
