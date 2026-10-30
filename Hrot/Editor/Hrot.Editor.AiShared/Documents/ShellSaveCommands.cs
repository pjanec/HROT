using System;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Registers the global <c>shell.save</c>, <c>shell.saveAs</c>, and <c>shell.saveAll</c>
/// commands into a supplied command set and holds the pure decision logic so it is
/// unit-testable with a mock/fake <see cref="AiDocumentManager"/>.
///
/// <para>
/// <b>Routing by kind (Save):</b>
/// <list type="bullet">
///   <item>No active document → no-op.</item>
///   <item>Empty <see cref="IEditableAsset.SourceFilePath"/> → <c>requestSaveAs(doc)</c>.</item>
///   <item>Otherwise dispatch by <see cref="AssetKind"/> to the matching per-kind save
///       delegate, then <see cref="AiDocument.MarkClean"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Save As:</b> invokes the injected <c>requestSaveAs</c> seam for the active document.
/// The full dialog is implemented in Phase 6; in this phase production passes a seam that
/// reports "not yet available" (DEC-9).
/// </para>
///
/// <para>
/// <b>Save All:</b> delegates to <see cref="SaveAllAiDocumentsCommand.Execute"/> with the
/// injected per-kind delegates. Only dirty documents with a non-empty source path are written.
/// </para>
/// </summary>
public static class ShellSaveCommands
{
    // ── Command identifiers ─────────────────────────────────────────────────────

    /// <summary>Id for the Save command: <c>"shell.save"</c> (Ctrl+S).</summary>
    public const string SaveId    = "shell.save";

    /// <summary>Id for the Save As command: <c>"shell.saveAs"</c> (no default key).</summary>
    public const string SaveAsId  = "shell.saveAs";

    /// <summary>Id for the Save All command: <c>"shell.saveAll"</c> (Ctrl+Shift+S).</summary>
    public const string SaveAllId = "shell.saveAll";

    /// <summary>⭐ CE-1443 B2 — <c>"shell.saveAsRecipe"</c>: <i>Save BTree 'x' as Recipe…</i> — the focused document written into its kind's recipes folder (<see cref="Recipes.RecipeFiles"/>), which that kind's <i>New …</i> lists.</summary>
    public const string SaveAsRecipeId = "shell.saveAsRecipe";

    // ⛔ CE-1443 part B — the "scenario.save" / "scenario.saveAs" commands are registered ONCE, by ScenarioMenuCommands (on every operator host). This class
    //   registered the same two ids a second time (last registration won) — deleted.

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the three save commands.
    /// Called once at editor startup by the composition root.
    /// </summary>
    /// <param name="register">
    /// Registration delegate: receives an <see cref="EditorCommandDescriptor"/> and its
    /// handler <see cref="Action{EditorCommandContext}"/>.
    /// In production this is <c>WindowManager.ShellCommands.Register</c>; in tests a
    /// recording lambda.
    /// </param>
    /// <param name="docManager">
    /// The live document manager; <see cref="Save"/> reads <see cref="AiDocumentManager.Active"/>
    /// and <see cref="SaveAll"/> iterates <see cref="AiDocumentManager.OpenDocuments"/>.
    /// </param>
    /// <param name="saveBlueprint">
    /// Saves a Blueprint asset via <c>SaveActiveBlueprintCommand.Save(asset, path)</c>.
    /// No-op when null.
    /// </param>
    /// <param name="saveBTree">
    /// Saves a BTree asset as JSON. No-op when null.
    /// </param>
    /// <param name="saveHsm">
    /// Saves an HSM asset as JSON. No-op when null.
    /// </param>
    /// <param name="saveScenario">
    /// Saves a Scenario asset (when the scenario asset kind exists). No-op when null.
    /// </param>
    /// <param name="requestSaveAs">
    /// Seam for Save-As dialog (Phase 6). In this phase, production passes a seam that
    /// reports "not yet available"; tests inject a recording seam.
    /// </param>
    /// <param name="report">
    /// Optional status reporter (e.g. for warnings on unsupported kinds). May be null.
    /// </param>
    /// <param name="isScenarioContext">
    /// When not null and returns <c>true</c>, the save/saveAs commands resolve the
    /// scenario as the active save target instead of the active document.
    /// </param>
    /// <param name="hasLoadedScenario">
    /// When not null, determines whether Save/Save-As is enabled in scenario context.
    /// </param>
    /// <param name="saveScenarioAction">
    /// Scenario save action. When not null and scenario context is active, shell.save
    /// delegates here instead of the per-kind document delegates.
    /// </param>
    /// <param name="requestScenarioSaveAs">
    /// Scenario Save-As action. When not null, shell.saveAs delegates here in scenario
    /// context, and the <c>scenario.saveAs</c> command is registered.
    /// </param>
    /// <param name="scenarioName">
    /// ⭐ CE-1443 part B — the loaded scenario's name, for the label in scenario context (<see cref="SaveLabels"/>: <i>Save Scenario 'x'</i>).
    /// ⛔ SUPERSEDES the editor-only <c>describeActiveTarget</c> seam: the label is ONE rule on every host, never a host's own text.
    /// </param>
    /// <param name="resolveActiveDocument">
    /// When not null, the non-scenario branch uses this to resolve the save target
    /// (and determine <c>IsEnabled</c>) instead of <see cref="AiDocumentManager.Active"/>.
    /// Return <c>null</c> to indicate that no document is targetable from the current
    /// perspective — Save/Save-As will be DISABLED and the handler is a no-op.
    /// When this parameter is null, <see cref="AiDocumentManager.Active"/> is used
    /// (back-compat: legacy behavior preserved for tests that omit this seam).
    /// </param>
    public static void Register(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> register,
        AiDocumentManager                                         docManager,
        SaveAllAiDocumentsCommand.SaveDelegate?                   saveBlueprint,
        SaveAllAiDocumentsCommand.SaveDelegate?                   saveBTree,
        SaveAllAiDocumentsCommand.SaveDelegate?                   saveHsm,
        SaveAllAiDocumentsCommand.SaveDelegate?                   saveScenario,
        Action<AiDocument>                                        requestSaveAs,
        Action<string>?                                           report = null,
        Func<bool>?                                               isScenarioContext = null,
        Func<bool>?                                               hasLoadedScenario = null,
        Action?                                                   saveScenarioAction = null,
        Action?                                                   requestScenarioSaveAs = null,
        Func<string?>?                                            scenarioName = null,
        Func<AiDocument?>?                                        resolveActiveDocument = null,
        SaveAllAiDocumentsCommand.SaveDelegate?                   saveBlueprintRecipe = null)
    {
        if (register       is null) throw new ArgumentNullException(nameof(register));
        if (docManager     is null) throw new ArgumentNullException(nameof(docManager));
        if (requestSaveAs  is null) throw new ArgumentNullException(nameof(requestSaveAs));

        // ⭐⭐ CE-1443 part B (AQ108 §2.15) — every save item NAMES what it saves (SaveLabels), on every host: the scenario in scenario context, else the focused document.
        AiDocument? Target() => resolveActiveDocument != null ? resolveActiveDocument() : docManager.Active;
        string Label(string verb, string suffix)
            => isScenarioContext?.Invoke() == true ? SaveLabels.Named(verb, "Scenario", scenarioName?.Invoke()) + suffix
             : Target() is { } d                   ? SaveLabels.For(verb, d) + suffix
             :                                       $"{verb}{suffix} (no document open)";

        // ── shell.save ──────────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          SaveId,
                DisplayName: "Save",
                Category:    "File",
                Description: "Save the active document (Ctrl+S)",
                IconKey:     "shell/save",
                DefaultKey:  new KeyBinding(EditorKey.S, KeyModifiers.Ctrl),
                IsEnabled:   () => (isScenarioContext?.Invoke() == true)
                    ? true
                    : (resolveActiveDocument != null ? resolveActiveDocument() : docManager.Active) is { IsReadOnly: false },
                DynamicDisplayName: () => Label("Save", "")),
            _ =>
            {
                // Branch on scenario context first.
                if (isScenarioContext?.Invoke() == true)
                {
                    if (hasLoadedScenario?.Invoke() == true) saveScenarioAction?.Invoke();
                    else                                     requestScenarioSaveAs?.Invoke();
                    return;
                }

                // Use perspective-scoped resolver when supplied; fall back to docManager.Active.
                var doc = resolveActiveDocument != null
                    ? resolveActiveDocument()
                    : docManager.Active;
                if (doc == null) return;
                if (doc.IsReadOnly) { report?.Invoke($"[INFO] {AiDocument.ReadOnlyReason(doc.Asset)}."); return; }   // ⭐ CE-1252

                if (string.IsNullOrEmpty(doc.Asset.SourceFilePath))
                {
                    requestSaveAs(doc);
                    return;
                }

                var path = doc.Asset.SourceFilePath;
                switch (doc.Kind)
                {
                    case AssetKind.Blueprint:
                        saveBlueprint?.Invoke(doc.Asset, path);
                        doc.MarkClean();
                        report?.Invoke($"[OK] Saved {doc.Kind}: '{doc.Asset.Name}'.");
                        break;

                    case AssetKind.BTree:
                        saveBTree?.Invoke(doc.Asset, path);
                        doc.MarkClean();
                        report?.Invoke($"[OK] Saved {doc.Kind}: '{doc.Asset.Name}'.");
                        break;

                    case AssetKind.Hsm:
                        saveHsm?.Invoke(doc.Asset, path);
                        doc.MarkClean();
                        report?.Invoke($"[OK] Saved {doc.Kind}: '{doc.Asset.Name}'.");
                        break;

                    default:
                        // Unsupported kind: report and skip; never throw.
                        report?.Invoke(
                            $"[WARN] Save unsupported kind '{doc.Kind}' for '{doc.Asset.Name}'.");
                        break;
                }
            });

        // ── shell.saveAs ────────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          SaveAsId,
                DisplayName: "Save As…",
                Category:    "File",
                Description: "Save the active document under a new name/path",
                IconKey:     "shell/saveAs",
                DefaultKey:  null,
                IsEnabled:   () => (isScenarioContext?.Invoke() == true)
                    ? true
                    : (resolveActiveDocument != null ? resolveActiveDocument() : docManager.Active) is { IsReadOnly: false },
                DynamicDisplayName: () => Label("Save", " As…")),
            _ =>
            {
                // Branch on scenario context first.
                if (isScenarioContext?.Invoke() == true)
                {
                    requestScenarioSaveAs?.Invoke();
                    return;
                }

                // Use perspective-scoped resolver when supplied; fall back to docManager.Active.
                var doc = resolveActiveDocument != null
                    ? resolveActiveDocument()
                    : docManager.Active;
                if (doc is { IsReadOnly: true }) { report?.Invoke($"[INFO] {AiDocument.ReadOnlyReason(doc.Asset)}."); return; }   // ⭐ CE-1252
                if (doc != null)
                    requestSaveAs(doc);
            });

        // ── shell.saveAsRecipe (CE-1443 B2) ─────────────────────────────────────
        //    ⭐ The kind's own writer into RecipeFiles.PathFor (never over an existing recipe); ⛔ the document is NOT marked clean — its own file was not saved.
        //    A Blueprint recipe needs the recipe card on the written copy, so it has its own writer (saveBlueprintRecipe); BTree / HSM reuse their save delegates.
        SaveAllAiDocumentsCommand.SaveDelegate? RecipeWriter(AssetKind kind) => kind switch
        {
            AssetKind.BTree     => saveBTree,
            AssetKind.Hsm       => saveHsm,
            AssetKind.Blueprint => saveBlueprintRecipe,
            _                   => null,
        };
        register(
            new EditorCommandDescriptor(
                Id:          SaveAsRecipeId,
                DisplayName: "Save as Recipe…",
                Category:    "File",
                Description: "Save the focused document as a recipe its kind's New … offers",
                IconKey:     null,
                DefaultKey:  null,
                IsEnabled:   () => isScenarioContext?.Invoke() != true && Target() is { IsReadOnly: false } d && RecipeWriter(d.Kind) != null,
                DynamicDisplayName: () => Target() is { } d && isScenarioContext?.Invoke() != true ? SaveLabels.For("Save", d) + " as Recipe…" : "Save as Recipe… (no document open)"),
            _ =>
            {
                if (isScenarioContext?.Invoke() == true || Target() is not { } doc || doc.IsReadOnly || RecipeWriter(doc.Kind) is not { } write) return;
                var path = Recipes.RecipeFiles.PathFor(doc.Kind, doc.Asset.Name);
                write(doc.Asset, path);
                report?.Invoke($"[OK] Saved {SaveLabels.KindName(doc.Kind)} recipe '{System.IO.Path.GetFileName(path)}'.");
            });

        // ── shell.saveAll ───────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          SaveAllId,
                DisplayName: "Save All",
                Category:    "File",
                Description: "Save all dirty open documents (Ctrl+Shift+S)",
                IconKey:     "shell/saveAll",
                DefaultKey:  new KeyBinding(EditorKey.S, KeyModifiers.Ctrl | KeyModifiers.Shift),
                IsEnabled:   () => DirtyCount(docManager) > 0,
                DynamicDisplayName: () => SaveLabels.SaveAll(DirtyCount(docManager))),   // ⭐ CE-1443 part B — Save All counts what it will write
            _ =>
            {
                SaveAllAiDocumentsCommand.Execute(
                    docManager,
                    saveBlueprint,
                    saveBTree,
                    saveHsm,
                    report);
            });
    }

    private static int DirtyCount(AiDocumentManager docManager)
    {
        int n = 0;
        var open = docManager.OpenDocuments;
        for (int i = 0; i < open.Count; i++)
            if (open[i].IsDirty) n++;
        return n;
    }
}
