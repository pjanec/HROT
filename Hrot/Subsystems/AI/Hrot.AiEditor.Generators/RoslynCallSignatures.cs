using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence.Emit;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐ <c>CE-504</c> C-1 — the generator's signature source for <see cref="BTreeCallShapes"/>: a bound method's parameter
/// list, read from the compilation. 📄 <c>docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md</c> §5 slice 1.
///
/// <para>⚠ A generated blueprint's <c>TickCore</c> is emitted by ANOTHER generator, so this compilation cannot see it.
/// A binding that still names one by FQN (a file written before CE-417 B-1, or a test) is recognised by the generated
/// class naming convention and given the TickCore parameter list — the same rule the validator's AiPrimitive fallback uses.</para>
/// </summary>
internal static class RoslynCallSignatures
{
    private static readonly SymbolDisplayFormat Fqn = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static readonly IReadOnlyList<CallParam> GeneratedTickCore = new[]
    {
        new CallParam("Params", true), new CallParam("WorkingState", true),
        new CallParam("Fdp.Core.Entity", false), new CallParam("Fdp.Core.EntityRepository", false),
        new CallParam("System.Single", false),
    };

    public static Func<string, IReadOnlyList<CallParam>?> Make(Compilation compilation)
    {
        return fqn =>
        {
            if (BTreeMethodCompatibilityValidator.ResolveMethod(compilation, fqn) is { } m)
                return m.Parameters
                        .Select(p => new CallParam(p.Type.ToDisplayString(Fqn), p.RefKind != RefKind.None,
                                                   p.RefKind == RefKind.Ref && SharedAiMethodResolver.IsUnitMemory(p.Type)))
                        .ToArray();
            // ⭐ The validator's own convention (TryValidateAsGeneratedBlueprintTickCore): a "{Name}_{id:X8}_Bp.TickCore"
            //   in ANY namespace is a generated blueprint's TickCore. The validator then checks it against the catalogue.
            const string suffix = ".TickCore";
            return fqn.EndsWith(suffix, StringComparison.Ordinal)
                   && GeneratedBlueprintSchemaCatalog.TryParseGeneratedClassRef(
                          fqn.Substring(0, fqn.Length - suffix.Length), out _, out _)
                ? GeneratedTickCore : null;
        };
    }
}
