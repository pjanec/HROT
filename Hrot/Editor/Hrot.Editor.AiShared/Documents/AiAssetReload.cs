using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// The ONE implementation of the "recompile the active AI document" POLICY, shared by every host.
///
/// <para>⭐⭐⭐ <b>What this owns</b> — and it is the half where the drift actually hurt:
/// <list type="bullet">
///   <item>the no-active-document arm;</item>
///   <item>the dispatch by <see cref="AssetKind"/>;</item>
///   <item>the default arm — a document with no compilable canvas context says SO;</item>
///   <item>the try/catch — <i>"a compile is user input; it must not take the node down"</i>;</item>
///   <item>⭐⭐ the origin-side LOG on every reload (ruling 53), which
///       <c>DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md</c> §10.4 calls
///       <i>"a requirement, not a nicety"</i>;</item>
///   <item>⭐⭐ every status STRING — the user-visible surface of this duplication.</item>
/// </list></para>
///
/// <para>⛔ <b>What this does NOT own:</b> the map from a host's concrete asset to a DTO, and the
/// compiler itself. Both name types this assembly cannot reference (a cycle — see
/// <see cref="AiAssetSavers"/>), so they arrive as an <see cref="AiReloadArms"/> and a
/// <see cref="CompileSources"/>.</para>
///
/// <para>⚠⚠ <b>Before this class</b>, 📐 measured <c>2026-08-27</c>: CGF had ONE method switching on the
/// canvas context's runtime type; the editor had THREE delegates plus a FOURTH inline <c>switch</c> in
/// its toolbar wiring — and the editor's path had <b>no</b> try/catch, <b>no</b> log and <b>no</b>
/// default arm. ⇒ ⭐ routing the editor here is the editor GAINING three behaviours, each with an
/// authority cited in design §5c.6.4 — not a refactor that changed its mind about anything.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.6 (decisions
/// <c>E2</c>–<c>E4</c>).</para>
/// </summary>
public static class AiAssetReload
{
    /// <summary>
    /// A compiler verdict, in terms this assembly can name. ⚠ Deliberately NOT
    /// <c>Hrot.Blueprints.Editor.Reload.QuickReloadResult</c> — that type lives on the far side of the
    /// reference cycle. Hosts adapt at the call site, which is one line.
    /// </summary>
    public readonly record struct CompileOutcome(bool Succeeded, string? ErrorMessage, long DurationMs);

    /// <summary>Compiles emitted sources into a patch assembly and returns the verdict.</summary>
    public delegate CompileOutcome CompileSources(
        IReadOnlyList<(string Source, string FileName)> sources, string assemblyName);

    /// <summary>
    /// Reloads the active document, whatever its kind.
    ///
    /// <para>⭐ Returns the status string rather than throwing: a failed compile is a legitimate outcome
    /// of editing, and the caller (toolbar or MCP) reports it.</para>
    /// </summary>
    /// <param name="documents">the host's document manager; a null/empty one is a status, not a throw.</param>
    /// <param name="arms">the per-kind bodies. ⚠ A null arm means "this host cannot compile that kind".</param>
    /// <param name="log">
    /// ⭐⭐ ruling 53's origin-side log, invoked in a <c>finally</c> with (asset name, status) on EVERY
    /// reload — success, failure and throw alike. ⛔ Optional only so unit rails need not supply one;
    /// <b>a production caller that has a logger must pass it</b> (the silent-default rule).
    /// </param>
    public static string Reload(
        AiDocumentManager? documents,
        AiReloadArms arms,
        Action<string, string>? log = null)
    {
        if (arms == null) throw new ArgumentNullException(nameof(arms));

        var active = documents?.Active;
        if (active == null)
            return Report(log, "(none)", NoActiveDocument);

        var status = NoCompilableContext(active.Asset.Name, active.Kind);
        try
        {
            var arm = active.Kind switch
            {
                AssetKind.Blueprint => arms.Blueprint,
                AssetKind.BTree     => arms.BTree,
                AssetKind.Hsm       => arms.Hsm,
                _                   => null,
            };

            // ⚠ A null arm, and an arm that cannot resolve its model, BOTH land on the shared
            //   NoCompilableContext text — which is how CGF's runtime-type dispatch and this
            //   kind dispatch stay byte-identical (design §5c.6 E4).
            status = arm?.Invoke() ?? status;
            return Report(log, active.Asset.Name, status);
        }
        catch (Exception ex)
        {
            // ⛔ A compile is user input; it must not take the node down. ⭐ Reported, not swallowed.
            status = $"Reload threw: {ex.Message}";
            return Report(log, active.Asset.Name, status);
        }
    }

    /// <summary>The BTree arm's body: DTO → topology + bridge sources → compile → status.</summary>
    public static string ReloadBTree(BehaviorTreeAssetDto dto, CompileSources compile)
    {
        if (dto == null)     throw new ArgumentNullException(nameof(dto));
        if (compile == null) throw new ArgumentNullException(nameof(compile));

        var topology = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitTopologyCore(dto);
        var bridge   = Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.EmitBridge(dto);
        var outcome  = compile(
            new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
            PatchAssemblyName("BTree", dto.AssetId));

        return Format("BTree", dto.Name, outcome);
    }

    /// <summary>The HSM arm's body — symmetric to <see cref="ReloadBTree"/>.</summary>
    public static string ReloadHsm(HsmAssetDto dto, CompileSources compile)
    {
        if (dto == null)     throw new ArgumentNullException(nameof(dto));
        if (compile == null) throw new ArgumentNullException(nameof(compile));

        var topology = Hrot.AiEditor.Persistence.Emit.HsmEmitCore.EmitTopologyCore(dto);
        var bridge   = Hrot.AiEditor.Persistence.Emit.HsmBridgeEmitCore.EmitBridge(dto);
        var outcome  = compile(
            new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
            PatchAssemblyName("Hsm", dto.AssetId));

        return Format("HSM", dto.Name, outcome);
    }

    /// <summary>
    /// The Blueprint arm's status formatter. ⚠ Blueprint has no emit step here — its compile runs
    /// entirely inside <c>Hrot.Blueprints.Editor</c> from the in-memory asset — so the host hands the
    /// verdict over and this class only owns the WORDING.
    /// </summary>
    public static string FormatBlueprint(string assetName, CompileOutcome outcome)
        => outcome.Succeeded
            ? $"Compiled blueprint '{assetName}' in {outcome.DurationMs}ms"
            : $"Blueprint compile failed: {outcome.ErrorMessage}";

    /// <summary>
    /// The one wording for "there is nothing here I can recompile". ⭐ Public because each host's arm
    /// returns it when its own model resolve fails — that is what keeps the two hosts identical.
    /// </summary>
    public static string NoCompilableContext(string assetName, AssetKind kind)
        => $"'{assetName}' ({kind}) has no compilable canvas context.";

    /// <summary>The one wording for "no document is open".</summary>
    public const string NoActiveDocument = "No active AI document to reload.";

    /// <summary>
    /// ⚠ The patch assembly name must stay unique per reload — the GUID is load-bearing, not
    /// decoration: two patches sharing a name cannot both be loaded.
    /// </summary>
    internal static string PatchAssemblyName(string kind, Guid assetId)
        => $"{kind}Patch_{assetId:N}_{Guid.NewGuid():N}";

    private static string Format(string kindLabel, string assetName, CompileOutcome outcome)
        => outcome.Succeeded
            ? $"Compiled {kindLabel} '{assetName}' in {outcome.DurationMs}ms"
            : $"{kindLabel} compile failed: {outcome.ErrorMessage}";

    private static string Report(Action<string, string>? log, string assetName, string status)
    {
        // ⭐⭐ RULING 53's origin-side log, on EVERY reload — not only the failures, and not only the
        //    Hard ones. ⛔ A headless node that silently recompiles the brain a live exercise is
        //    running is exactly what the ruling's safety net is for. ⚠ The Soft/Hard distinction is
        //    NOT available on this path (CE-023), so the log records the ACT, not a classification it
        //    cannot honestly make.
        log?.Invoke(assetName, status);
        return status;
    }
}

/// <summary>
/// The per-kind reload bodies a host can supply. ⚠ A null MEMBER is an honest "this host cannot
/// compile that kind" — ⛔ NOT a host conditional inside the shared policy (ruling 58).
///
/// <para>⭐⭐ An arm that RETURNS null means "I am the right kind, but there is no model to compile" —
/// and <see cref="AiAssetReload.Reload"/> then supplies
/// <see cref="AiAssetReload.NoCompilableContext"/>. ⇒ ⛔ no arm has to know that wording, which is what
/// keeps CGF's old runtime-type dispatch and the editor's kind dispatch byte-identical.</para>
/// </summary>
public sealed record AiReloadArms(
    Func<string?>? Blueprint = null,
    Func<string?>? BTree = null,
    Func<string?>? Hsm = null);
