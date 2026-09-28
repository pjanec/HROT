using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Roslyn IncrementalGenerator that consumes <c>*.hsm.json</c> AdditionalTexts
/// and emits <c>CreateBuilder()</c> + <c>[HsmDefinition]</c> thunk (NO <c>[HsmLayout]</c>)
/// to <c>obj/GeneratedFiles/{Name}.g.cs</c>.
///
/// Design §6.2 (PU-202): JSON-owned assets generate topology core only; layout lives in JSON.
/// Per-asset deserialization failure → Roslyn diagnostic (never throws, never fails siblings).
/// Mirrors <see cref="Hrot.Blueprints.Generators.BlueprintIncrementalGenerator"/> control flow.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HsmJsonGenerator : IIncrementalGenerator
{
    /// <summary>Diagnostic code for HSM JSON parse/emit errors.</summary>
    public const string DiagnosticId = "HSM0001";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider: raw file text from *.hsm.json AdditionalTexts
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(static at => at.Path.EndsWith(".hsm.json",
                    System.StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) =>
                {
                    string text = at.GetText(ct)?.ToString() ?? string.Empty;
                    return (at.Path, text);
                });

        // ⭐ BP-281: combine with the full compilation so the bridge can resolve struct-DTO sizes for a
        //   managed blackboard — the SAME seam BTreeJsonGenerator uses (StructSizeResolver).
        //   ⛔ Without it a struct-typed HSM input variable would be unsizeable and the params supply
        //   would silently not be emitted: exactly the "caller HAS the dependency and does not pass it"
        //   shape this codebase files as a defect.
        //   Incrementality note: as on the BTree side, this makes GenerateOneAsset re-run on ANY
        //   compilation change. Acceptable for the small *.hsm.json asset set (VE-DEBT-003).
        // ⭐⭐⭐ CE-384 — the *.bp.json AdditionalTexts, so a state's activity or a transition's guard
        //   can BE a blueprint. 🔴 This is the ONLY way to learn a blueprint's BlueprintId from here:
        //   the Blueprint generator and this one are SIBLING Roslyn generators driven from the same
        //   PRE-generation compilation, so the generated {Name}_{BlueprintId:X8}_Bp class is
        //   unresolvable by symbol "not even in a fully successful real build"
        //   (GeneratedBlueprintSchemaCatalog's own header). ⭐ Same providers the BTree generator
        //   already collects for the AiPrimitiveTickCore composition path.
        IncrementalValueProvider<ImmutableArray<(string Path, string Text)>> bpJsonCollected =
            context.AdditionalTextsProvider
                .Where(static at => at.Path.EndsWith(".bp.json",
                    System.StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) =>
                {
                    string text = at.GetText(ct)?.ToString() ?? string.Empty;
                    return (at.Path, text);
                })
                .Collect();

        IncrementalValuesProvider<(string Path, string Text, Compilation Compilation, ImmutableArray<(string Path, string Text)> BpJsonFiles)> combined =
            rawFiles.Combine(context.CompilationProvider)
                    .Combine(bpJsonCollected)
                    .Select(static (pair, _) =>
                        (pair.Left.Left.Path, pair.Left.Left.Text, pair.Left.Right, pair.Right));

        // Per-asset: deserialize → emit topology core → register source output
        context.RegisterSourceOutput(combined, static (spc, item) =>
        {
            GenerateOneAsset(spc, item.Path, item.Text, item.Compilation, item.BpJsonFiles);
        });
    }

    private static void GenerateOneAsset(
        SourceProductionContext spc, string path, string text, Compilation compilation,
        ImmutableArray<(string Path, string Text)> bpJsonFiles)
    {
        // Deserialize — failure becomes a diagnostic, never throws, never fails siblings.
        HsmAssetDto? dto;
        try
        {
            dto = HsmJsonServices.Deserialize(text);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during deserialization: " + ex.Message));
            return;
        }

        if (dto is null)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Deserialization returned null (empty or invalid JSON)."));
            return;
        }

        // BP-281 / E7b: the Roslyn-backed struct-size resolver, built once and used by BOTH
        // emitters — the topology core bakes expression-target offsets into action keys and the
        // bridge writes ParseParams at the same offsets, so they must resolve sizes identically.
        // Only built when there is a managed blackboard to size; otherwise null, and the emitted
        // output is byte-identical to before.
        System.Func<string, int?>? sizeResolver =
            dto.Blackboard != null && dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0
                ? StructSizeResolver.MakeDelegate(compilation)
                : null;

        // ⭐⭐ CE-384 — Guid → the ushort the blueprint's generated thunk registers under.
        //   ⛔ Returns null for an unknown asset id rather than 0: a 0 would be a VALID action id and
        //   would silently mis-dispatch, which is the failure mode this whole id story exists to
        //   avoid. The emitter then emits nothing and the validator is what complains.
        //   ⚠ Built even when there are no blueprints — Parse() on an empty array is empty, and the
        //   emitted output is byte-identical because no DTO names a blueprint.
        var blueprintSchemas = GeneratedBlueprintSchemaCatalog.Parse(bpJsonFiles);
        System.Func<System.Guid, ushort?> blueprintIdResolver = assetId =>
        {
            foreach (var schema in blueprintSchemas)
            {
                if (schema.AssetId != assetId) continue;
                // ⚠ A Library or Instance blueprint emits no HsmActivity/HsmGuard thunk, so resolving
                //   one would bake an id nothing registers — a silent TryGetValue miss at dispatch,
                //   which is the exact failure E6 spent a batch on.
                if (!schema.IsAiPrimitive) return null;
                return unchecked((ushort)schema.BlueprintId);
            }
            return null;
        };

        // ⭐⭐⭐ CE-388 / Q74 D-B1 — the same lookup, returning the GENERATED CLASS NAME.
        //
        //   🔒 That string is what BOTH sides hash to get the exit-cleanup id: CSharpEmitter
        //      registers the thunk under HsmActionKey.ForExitCleanup(className), and HsmEmitCore
        //      bakes .OnExitId(<the same expression>) into the blob. The formula itself is a LINKED
        //      file (Q74 D-F) precisely so there is one of it.
        //   ⛔ Same null discipline as above, and for a sharper reason: a non-AiPrimitive emits no
        //      HsmExitCleanup thunk at all, so resolving one would bake an id nothing registered —
        //      and the ONLY symptom would be a channel that is never released. CE-403 measured that
        //      exact class of silent miss in the sibling C# table.
        System.Func<System.Guid, string?> blueprintClassNameResolver = assetId =>
        {
            foreach (var schema in blueprintSchemas)
            {
                if (schema.AssetId != assetId) continue;
                if (!schema.IsAiPrimitive) return null;
                return schema.GeneratedClassName;
            }
            return null;
        };

        // ⭐⭐⭐ CE-388 / Q74 D-D1 — "does this C# activity declare [WritesChannel]?"
        //
        //   🔒 User, 2026-09-28: "same auto-bind" — one rule for both routes, so a C# activity that
        //      writes a channel releases it on exit without the author binding anything.
        //   ⭐ Answered from the Roslyn COMPILATION, which is the only thing that knows: the
        //      attribute lives on the method symbol, and no artefact carries it to the emitter.
        //      (This is the same capability RoslynClrSignatureResolver already demonstrates.)
        //   ⛔ Cached per compilation, not per state — GetTypeByMetadataName is not free and an
        //      asset can bind the same activity in many states.
        var writesChannelCache = new System.Collections.Generic.Dictionary<string, bool>(System.StringComparer.Ordinal);
        System.Func<string, bool> csharpWritesChannel = actionFqn =>
        {
            if (writesChannelCache.TryGetValue(actionFqn, out bool cached)) return cached;

            bool result = false;
            int dot = actionFqn.LastIndexOf('.');
            if (dot > 0)
            {
                string typeName   = actionFqn.Substring(0, dot);
                string methodName = actionFqn.Substring(dot + 1);
                var type = compilation.GetTypeByMetadataName(typeName);
                if (type != null)
                {
                    foreach (var member in type.GetMembers(methodName))
                    {
                        foreach (var attr in member.GetAttributes())
                        {
                            // ⭐ The SAME comparison HsmActionGenerator.cs:129 makes. ⛔ Not a
                            //   name-only check: an unrelated [WritesChannel] in another namespace
                            //   must not turn on a cleanup binding.
                            if (attr.AttributeClass?.ToDisplayString() == "Fbt.Kernel.WritesChannelAttribute")
                            {
                                result = true;
                                break;
                            }
                        }
                        if (result) break;
                    }
                }
            }

            writesChannelCache[actionFqn] = result;
            return result;
        };

        // Emit topology core (CreateBuilder + [HsmDefinition] thunk, NO [HsmLayout]).
        string source;
        try
        {
            source = HsmEmitCore.EmitTopologyCore(dto, sizeResolver, blueprintIdResolver,
                                                 blueprintClassNameResolver, csharpWritesChannel);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during code generation: " + ex.Message));
            return;
        }

        string baseName = System.IO.Path.GetFileNameWithoutExtension(
                              System.IO.Path.GetFileNameWithoutExtension(path));

        // Topology core: {Name}.g.cs
        spc.AddSource(baseName + ".g.cs", source);

        // Bridge: {Name}.Registrar.g.cs  (additive, separate hint name — PU-203, §14 item 3)
        string bridge;
        try
        {
            bridge = HsmBridgeEmitCore.EmitBridge(dto, sizeResolver);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during bridge code generation: " + ex.Message));
            return;
        }

        spc.AddSource(baseName + ".Registrar.g.cs", bridge);

        // ⭐⭐⭐ Batch 92 (92b): orchestrators — {Name}.Orchestrators.g.cs
        //
        // ⛔ OMITTED ENTIRELY when the core returns null, which is every asset in today's corpus
        // (none carries an alias) ⇒ the generated output stays byte-identical.
        //
        // ⭐⭐ This is the arm 91b made meaningful: HSM hosts a sub-tree ONLY through an Approach-A
        // alias, and until aliases persisted, nothing an HSM loaded from disk could ever emit.
        // ⛔ It is not "HSM sub-tree hosting is complete" — there is still no authoring gesture that
        // creates the alias, and no blackboard aggregation behind it.
        string? orchestrators;
        try
        {
            orchestrators = HsmOrchestratorEmitCore.Emit(dto);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during orchestrator code generation: " + ex.Message));
            return;
        }

        if (orchestrators != null)
            spc.AddSource(baseName + ".Orchestrators.g.cs", orchestrators);
    }

    /// <summary>Creates a Roslyn diagnostic for an HSM JSON parse/emit error.</summary>
    internal static Diagnostic MakeParseErrorDiagnostic(string path, string detail)
    {
        // Descriptor created inline to avoid RS2008 (release tracking required for static fields).
        // Mirrors BlueprintIncrementalGenerator.ToRoslynDiagnostic pattern.
        var descriptor = new DiagnosticDescriptor(
            id:                 DiagnosticId,
            title:              "HSM JSON parse error",
            messageFormat:      "Failed to process '{0}': {1}",
            category:           "HsmJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path, detail);
    }
}
