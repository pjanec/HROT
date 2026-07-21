using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Generators;

/// <summary>
/// <see cref="IClrSignatureResolver"/> backed by the Roslyn <see cref="Compilation"/>'s semantic model.
/// <para>
/// The incremental generator compiles the game assembly (<c>Hrot.AI.Behaviors</c> etc.) that also
/// DEFINES the curated helper types a blueprint's FunctionCall nodes target. Runtime CLR reflection
/// cannot see those types (the assembly does not exist yet), which is why Stage0's reflection path
/// fails inside the analyzer host and every same-assembly-helper blueprint historically had to persist
/// explicit pins. The semantic model, in contrast, sees every source-declared type in the current
/// compilation, so this resolver hands Stage0 the real signature and the blueprints round-trip with no
/// explicit pins (the editor strips pins on save — Blocker-1 fix).
/// </para>
/// <para>
/// Type FQNs are rendered in <see cref="System.Type.FullName"/> convention (namespace-qualified, no
/// <c>global::</c> prefix, metadata names for special types so <c>int</c> → <c>System.Int32</c>) so the
/// pin TypeIds match those the reflection path produced and <c>StaticTypeRegistry</c> resolves them
/// identically. Overloads are NOT disambiguated (a FunctionCall node carries only TargetTypeId +
/// MethodName) — curated helpers are named uniquely to keep name-only resolution unambiguous.
/// </para>
/// </summary>
internal sealed class RoslynClrSignatureResolver : IClrSignatureResolver
{
    private readonly Compilation _compilation;

    // System.Type.FullName-equivalent rendering: fully namespace-qualified, no "global::", and NO
    // special-type keyword aliasing (default miscellaneousOptions) so e.g. System.Int32 (not "int").
    private static readonly SymbolDisplayFormat FullNameFormat = new SymbolDisplayFormat(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public RoslynClrSignatureResolver(Compilation compilation) => _compilation = compilation;

    public bool TryResolve(string targetTypeId, string methodName, out ClrMethodSig? sig)
    {
        sig = null;
        if (string.IsNullOrEmpty(targetTypeId) || string.IsNullOrEmpty(methodName))
            return false;

        // GetTypeByMetadataName wants the metadata name (no "global::" sentinel the editor may persist).
        var metadataName = targetTypeId.StartsWith("global::", System.StringComparison.Ordinal)
            ? targetTypeId.Substring("global::".Length)
            : targetTypeId;

        var type = _compilation.GetTypeByMetadataName(metadataName);
        if (type is null)
            return false;

        IMethodSymbol? method = null;
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is IMethodSymbol m)
            {
                method = m;
                break; // name-only resolution; helpers are uniquely named (see class doc).
            }
        }
        if (method is null)
            return false;

        var parameters = new List<ClrParamInfo>(method.Parameters.Length);
        foreach (var p in method.Parameters)
            parameters.Add(new ClrParamInfo(p.Name, p.Type.ToDisplayString(FullNameFormat)));

        var returnType = method.ReturnsVoid ? null : method.ReturnType.ToDisplayString(FullNameFormat);
        sig = new ClrMethodSig(parameters, returnType);
        return true;
    }
}
