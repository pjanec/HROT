using System;
using System.Text;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐ <c>CE-428</c> — THE one spelling, outside the blueprint compiler, of "which generated class does a
/// blueprint asset become": <c>global::Hrot.AI.Behaviors.Generated.{Sanitized}_{BlueprintId:X8}_Bp</c>.
///
/// <para>⚠ It MIRRORS the compiler — <c>Sanitizer.SanitizeName</c> and <c>BlueprintIdHash.Compute</c>
/// (FNV-1a 32 over <c>Guid.ToByteArray()</c>) — because the BTree/HSM generators must not reference
/// <c>Hrot.Blueprints.Compiler</c> (sibling Roslyn generators, <c>GeneratedBlueprintSchemaCatalog</c>'s
/// remarks). ⛔ It replaces the catalog's private copy rather than adding a third one; the C# compile of
/// the emitted call is the backstop if the two ever drift (an unknown class is CS0234, never silent).</para>
/// </summary>
public static class BlueprintClassNaming
{
    /// <summary>The fixed namespace every blueprint class is emitted into (<c>LibraryEmitter</c>).</summary>
    public const string Namespace = "Hrot.AI.Behaviors.Generated";

    /// <summary>⭐ <c>global::</c>-qualified FQN of the class a blueprint asset compiles to.</summary>
    public static string ClassFqn(Guid assetId, string assetName)
        => "global::" + Namespace + "." + ClassName(assetId, assetName);

    /// <summary>⭐ <c>CE-417</c> B-1 — the generated <c>TickCore</c> a blueprint binding calls, namespace-qualified WITHOUT
    /// <c>global::</c> (the form BTree assets have always carried in <c>MethodFqn</c>), built from a class name.</summary>
    public static string TickCoreFqn(string className) => Namespace + "." + className + ".TickCore";

    /// <summary>⭐ <c>CE-417</c> B-1 — the same, from the asset's id and name.</summary>
    public static string TickCoreFqn(Guid assetId, string assetName) => TickCoreFqn(ClassName(assetId, assetName));

    /// <summary>The unqualified class name.</summary>
    public static string ClassName(Guid assetId, string assetName)
        => $"{SanitizeName(assetName)}_{ComputeBlueprintId(assetId):X8}_Bp";

    /// <summary>Mirrors <c>Hrot.Blueprints.Core.Compiler.Emit.Sanitizer.SanitizeName</c>.</summary>
    public static string SanitizeName(string name)
        => global::Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer.PascalJoin(name, "UnknownBlueprint");

    /// <summary>THE asset-id hash (⭐ CE-2036: <c>Shared/BlueprintIdFnv.cs</c>, linked — was a mirror).</summary>
    public static int ComputeBlueprintId(Guid assetId) => global::Fdp.Toolkit.Behavior.Shared.BlueprintIdFnv.Compute(assetId);
}
