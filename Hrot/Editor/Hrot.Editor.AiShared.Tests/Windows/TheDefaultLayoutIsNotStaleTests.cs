using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 103 (<c>103b</c>) — THE SHIPPED DEFAULT LAYOUT IS NOT STALE.</b>
///
/// <para>⭐⭐ <b>The failure this prevents is certain, not hypothetical.</b> <c>layout/default/
/// fdp_windows.json</c> names <b>55 window ids</b>. ⛔ Rename or retire a window and its entry
/// <b>silently orphans</b>: the layout still loads, that window simply never appears, and
/// <c>WindowManager.LoadSettings</c> skips unknown ids <b>by design</b> *(<i>"unknown id — silently
/// skip"</i>)* ⇒ ⭐ <b>nothing anywhere says so.</b></para>
///
/// <para>⭐⭐ <b>Both directions matter, and they catch different things:</b>
/// <list type="bullet">
///   <item><b>orphan</b> — an id in the file that no window claims ⇒ a dead entry, and a window the
///   designer expected to be positioned that is not</item>
///   <item><b>unlisted</b> — a window the registrars create that the file never mentions ⇒ it opens
///   wherever ImGui feels like, which is what "a new window appears floating in the middle" is</item>
/// </list></para>
///
/// <para>⚠⚠ <b>WHICH LAYER THIS FAKES</b> *(📌 <c>M-29</c>)*. ⭐ It builds the three PRODUCTION
/// <c>PerspectiveWorkspaceRegistrar</c>s and registers them into a real <c>WindowManager</c>, so the
/// <c>ai_*</c> families are enumerated <b>behaviourally</b> — ⛔ and that is deliberate rather than a
/// text scan, because 📐 <b>28 of the 55 ids appear NOWHERE as string literals</b>: they are composed
/// at runtime as <c>$"ai_details_{suffix}"</c>. ⇒ ⛔ a grep-based rail would have reported 28 false
/// orphans.</para>
///
/// <para>⛔⛔ <b>What it CANNOT enumerate:</b> windows created by <c>EditorSubsystem</c> and the other
/// subsystems — <c>editor_*</c>, <c>excon_*</c>, <c>orchestrator_*</c>, the canvases — because
/// <b><c>EditorSubsystem</c> cannot be constructed headless</b> *(established Batch 100)*. ⭐ Those ids
/// fall to two weaker judgements over production SOURCE TEXT — a literal, then an interpolated-id
/// prefix — and <see cref="BuildOrphanReport"/> reports the COUNT PER JUDGEMENT, ⛔ so a green here is
/// never mistaken for full coverage.</para>
/// </summary>
public sealed class TheDefaultLayoutIsNotStaleTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    /// <summary>⭐ The perspectives the AI registrars own — the suffixes in the shipped file.</summary>
    private static readonly string[] Perspectives = { "BTree", "HSM", "Blueprint" };

    // ══ the file itself ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>It parses, it has windows, and the DOCKING BLOCK is present in the ini.</b>
    /// ⚠ The docking tree is the half that positions anything — ⛔ a <c>fdp_windows.json</c> with no
    /// <c>imgui.ini</c> beside it restores open/closed state into a default arrangement, which looks
    /// like the layout "not working" rather than like a missing file.
    /// </summary>
    [Fact]
    public void TheShippedLayoutParsesAndCarriesADockingTree()
    {
        var dir = ShippedLayoutDirectory();
        if (dir is null) return;                       // ⭐ see ShippedLayoutDirectory

        Assert.NotEmpty(ShippedWindowIds());

        var ini = File.ReadAllText(Path.Combine(dir, "imgui.ini"));
        Assert.True(ini.Contains("[Docking][Data]", StringComparison.Ordinal),
            "layout/default/imgui.ini has no [Docking][Data] block, so it carries window positions but "
          + "no docking tree — every window would restore floating.");
    }

    // ══ ORPHANS — an id in the file that nothing creates ═════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ORPHAN RAIL.</b> Every id in the shipped layout must be claimed by something.
    ///
    /// <para>⚠⚠ <b>Expect this to have something to say the first time</b> — the file is a snapshot of
    /// one session's windows, and 📌 the handoff predicted <c>Entity Blueprints</c>: a TITLE-shaped id
    /// among otherwise snake_case ones. ⭐ <b>The list is the deliverable</b>; ⛔ pruning the user's file
    /// to make this green would be deleting the evidence.</para>
    /// </summary>
    [Fact]
    public void EveryIdInTheShippedLayoutIsClaimedBySomething()
    {
        if (ShippedLayoutDirectory() is null) return;

        var report = BuildOrphanReport();

        Assert.True(report.Orphans.Count == 0,
            "The shipped default layout names windows that nothing creates:\n"
          + string.Join("\n", report.Orphans.Select(o => $"    '{o}' — no registrar creates it and no "
                                                       + "production source mentions it"))
          + "\n\n  Judged behaviourally (the three AI registrars): "
          + $"{report.RegistrarClaimed.Count} ids.\n"
          + $"  Judged by a literal in production source: {report.TextClaimed.Count} ids.\n"
          + $"  Judged by an interpolated-id prefix (weakest): {report.PrefixClaimed.Count} ids.\n"
          + "  Fix the WINDOW or the LAYOUT deliberately — do not prune the file to silence this.");
    }

    // ══ UNLISTED — a window the registrars create that the file omits ════════

    /// <summary>
    /// ⭐⭐ <b>THE OTHER DIRECTION.</b> Every window the three AI registrars create should have an entry,
    /// ⛔ or it restores wherever ImGui last left it — which for a NEW window is "floating in the
    /// middle of the screen".
    ///
    /// <para>⚠ Scoped to what this rail can ENUMERATE. ⛔ It cannot speak for the subsystem windows, and
    /// pretending otherwise would make a green here mean more than it does.</para>
    /// </summary>
    [Fact]
    public void EveryWindowTheAiRegistrarsCreateIsInTheShippedLayout()
    {
        if (ShippedLayoutDirectory() is null) return;

        var shipped  = ShippedWindowIds();
        var created  = RegistrarWindowIds();
        var unlisted = created.Where(id => !shipped.Contains(id)).OrderBy(x => x).ToList();

        Assert.True(unlisted.Count == 0,
            "These windows are created but have no entry in the shipped default layout, so they "
          + "restore un-docked:\n"
          + string.Join("\n", unlisted.Select(u => $"    {u}"))
          + "\n\n  Re-save the default from a session where they are placed "
          + "(File > Layout > Save current as default).");
    }

    // ── the two halves ──────────────────────────────────────────────────────

    private sealed record OrphanReport(
        IReadOnlyList<string> Orphans,
        IReadOnlyList<string> RegistrarClaimed,
        IReadOnlyList<string> TextClaimed,
        IReadOnlyList<string> PrefixClaimed);

    /// <summary>
    /// ⭐⭐ <b>THREE judgements, from strongest to weakest</b>, and the report names which one each id
    /// got — ⛔ so a green never reads as more coverage than it is.
    ///
    /// <para>⚠⚠ <b>The PREFIX half was added after the rail's first run reported three FALSE orphans.</b>
    /// 📐 <c>ai_canvas_btree|hsm|blueprint</c> are created by <c>AiGraphCanvasWindow</c> as
    /// <c>$"ai_canvas_{assetKind.ToLowerInvariant()}"</c> — ⭐ real windows, ⛔ composed at runtime and
    /// outside the three registrars, so neither of the first two halves could see them. ⇒ ⭐ the fix was
    /// to make the fallback able to read an INTERPOLATED id, ⛔ not to relax the assertion.</para>
    ///
    /// <para>⚠ <b>What the prefix half cannot do:</b> tell <c>ai_canvas_hsm</c> from a suffix that no
    /// longer exists — it proves the FAMILY is still built. ⭐ Acceptable only because every id it
    /// judges is one the behavioural half does not create; ⛔ for anything the registrars own, the
    /// behavioural answer wins.</para>
    /// </summary>
    private OrphanReport BuildOrphanReport()
    {
        var shipped  = ShippedWindowIds();
        // ⭐ CE-082 — TWO behavioural sources now: the three AI registrars, and the diagnostics bundle.
        var created  = RegistrarWindowIds();
        created.UnionWith(DiagnosticsBundleWindowIds());

        var claimedByRegistrar = shipped.Where(created.Contains).ToList();
        var rest               = shipped.Where(id => !created.Contains(id)).ToList();

        // ⭐ Half 2 — a literal id in production source. ⚠ Proves the string exists, ⛔ not that a
        //   window is registered under it.
        var sources = ProductionSourceText();
        var claimedByText = rest.Where(id => sources.Contains($"\"{id}\"", StringComparison.Ordinal))
                                .ToList();

        // ⭐ Half 3 — an INTERPOLATED id: the literal prefix up to the last '_', as it is written in
        //   the source (`$"ai_canvas_{…}"`). ⛔ Only for ids the first two halves did not claim.
        var claimedByPrefix = rest
            .Where(id => !claimedByText.Contains(id))
            .Where(id => id.LastIndexOf('_') > 0
                      && sources.Contains($"\"{id[..(id.LastIndexOf('_') + 1)]}{{",
                                          StringComparison.Ordinal))
            .ToList();

        var orphans = rest
            .Where(id => !claimedByText.Contains(id) && !claimedByPrefix.Contains(id))
            .OrderBy(x => x).ToList();

        return new OrphanReport(orphans, claimedByRegistrar, claimedByText, claimedByPrefix);
    }

    /// <summary>⭐ The ids the three production registrars actually register, asked of a real
    /// <c>WindowManager</c> — ⛔ never re-derived from the id convention.</summary>
    private HashSet<string> RegistrarWindowIds()
    {
        var wm = new Fdp.Presentation.WindowManager.WindowManager(_atlas);

        foreach (var perspective in Perspectives)
        {
            var services = new PerspectiveWorkspaceServices(
                new AssetCatalog(), new NoRefactor(), new DebugSessionRegistry(),
                new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
                isSimUp: () => false, isFrozen: () => false)
            {
                // ⚠ Without one the registrar creates NO Watch and NO Breakpoints window — 📌 measured
                //   in Batch 102 — and this rail would report two false orphans per perspective.
                BreakpointManager = new InertBreakpoints(),
            };

            services.CreateRegistrar(
                perspective, new EditorSelectionStore(),
                validators: Array.Empty<IAssetValidator>())
                .RegisterWindows(wm);
        }

        return wm.RegisteredWindowIds.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-082</c> — the ids <see cref="Hrot.Presentation.Windows.DiagnosticsWindowsBundle"/>
    /// registers, asked of a real <c>WindowManager</c>.
    ///
    /// <para>📐 <b>Why this exists.</b> The four diagnostics windows used to be claimed by this rail's
    /// WEAKEST judgement — a literal <c>"editor_fdp_inspector"</c> in <c>EditorSubsystem.cs</c>. Phase-2
    /// slice ② replaced those literals with ONE derived scheme, so the literals vanished and the orphan
    /// rail reported THREE false orphans. ⛔ The windows never moved — the DETECTION did.</para>
    ///
    /// <para>⭐⭐ Unlike <c>EditorSubsystem</c> *(which cannot be constructed headless — the whole reason
    /// the weak judgements exist)*, the BUNDLE composes fine here, so this is a behavioural answer and
    /// strictly stronger than the grep it replaces. ⛔ NOT
    /// <c>DiagnosticsWindowsBundle.InspectorId("editor_")</c>: re-deriving from the convention is what
    /// this rail refuses to do.</para>
    ///
    /// <para>⚠⚠ <b>Used ONLY by the ORPHAN direction</b> *(is this shipped id claimed by something?)*.
    /// ⛔ Deliberately NOT by <see cref="EveryWindowTheAiRegistrarsCreateIsInTheShippedLayout"/>: that
    /// rail's scope is the AI registrars, and its own remarks say it cannot speak for subsystem windows.
    /// 📌 Measured while wiring this: including it there immediately reddened on
    /// <c>editor_system_profiler</c> — <b>a REAL finding</b> *(that window has no entry in the shipped
    /// layout, so it restores un-docked)*, but a finding about SUBSYSTEM windows, which needs a windowed
    /// layout re-save rather than a scope this rail never claimed. Filed as <c>CE-087</c>.</para>
    /// </summary>
    private HashSet<string> DiagnosticsBundleWindowIds()
    {
        var wm = new Fdp.Presentation.WindowManager.WindowManager(_atlas);
        // ⭐⭐⭐ CE-082 (phase 2 slice ②) — THE DIAGNOSTICS IDS ARE NOW BEHAVIOURAL TOO.
        //    📐 They used to be claimed by the WEAKEST judgements — a literal `"editor_fdp_inspector"` in
        //    `EditorSubsystem.cs`. Slice ② replaced those literals with ONE derived scheme
        //    (`$"{IdPrefix}fdp_inspector"`), so the literal vanished and this rail reported THREE false
        //    orphans. ⛔ The windows never went anywhere — the DETECTION did.
        // ⭐⭐ Unlike `EditorSubsystem` (which cannot be constructed headless — the reason the weak
        //    judgements exist at all), the BUNDLE composes fine here. ⇒ we ask it what it registers
        //    instead of grepping for a string, which is strictly stronger than what it replaces.
        // ⛔ NOT `DiagnosticsWindowsBundle.InspectorId("editor_")` — that would re-derive the ids from
        //    the convention, which is exactly what this rail refuses to do.
        Fdp.Toolkit.Runner.UiBundleHost.Compose(
            new Fdp.Toolkit.Runner.IUiBundle[]
            {
                new Hrot.Presentation.Windows.DiagnosticsWindowsBundle(
                    new Hrot.Presentation.Windows.DiagnosticsHostServices(
                        IdPrefix:       "editor_",
                        TitlePrefix:    "Editor",
                        Perspective:    "Scenario",
                        Inspector:      new Fdp.Presentation.Panels.EntityInspectorPanel(),
                        RepoAdapter:    () => null,
                        InspectorState: () => new Fdp.Presentation.Abstractions.InspectorState(),
                        EventBrowser:   new Fdp.Presentation.Panels.EventBrowserPanel(),
                        TitleBarColor:  null,
                        // ⚠ Both non-null on purpose: the editor registers these two only when it has a
                        //   kernel, and the shipped layout NAMES them — so the layout's expectation is
                        //   the kernel-present case, and that is the case this rail must cover.
                        ArchitecturePanel: new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                                               new InertArchitectureService()),
                        ExecutionStats:    () => null)),
            },
            new Fdp.Toolkit.Runner.UiBundleContext(wm));

        return wm.RegisteredWindowIds.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>⚠ <see cref="Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel"/> rejects a null
    /// service, so the stub is real. ⭐ Never drawn — only the window ID is being asked for.</summary>
    private sealed class InertArchitectureService : Fdp.ModuleHost.Diagnostics.IArchitectureDiagnosticsService
    {
        public Fdp.ModuleHost.Diagnostics.ArchitectureSnapshotDto GetSnapshot() => new();
    }


    private static HashSet<string> ShippedWindowIds()
    {
        var dir = ShippedLayoutDirectory();
        if (dir is null) return new HashSet<string>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "fdp_windows.json")));
        return doc.RootElement.GetProperty("Windows").EnumerateObject()
                  .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>⭐ Every production <c>.cs</c> under the two source roots, concatenated once.
    /// ⛔ Tests and generated output excluded — a test naming an id does not make it real.</summary>
    private static string ProductionSourceText()
    {
        var root = RepoRoot();
        if (root is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var dir in new[] { "Hrot", "FDP" })
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)) continue;
                if (f.Contains("Tests", StringComparison.Ordinal)) continue;
                sb.Append(File.ReadAllText(f));
            }
        }
        return sb.ToString();
    }

    /// <summary>⚠ <c>null</c> when the tests are not running from a checkout — ⭐ the layout rails then
    /// no-op rather than failing for a reason that is not about the layout.</summary>
    private static string? ShippedLayoutDirectory()
    {
        var root = RepoRoot();
        if (root is null) return null;
        var dir = Path.Combine(root, "layout", "default");
        return Directory.Exists(dir) ? dir : null;
    }

    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "layout", "default"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    // ── the two inert services the registrar requires ───────────────────────

    private sealed class InertBreakpoints : Hrot.Diagnostics.Breakpoints.IDataBreakpointManager
    {
        public void StageFieldMutation(Fdp.Core.Entity e, Type t, int off, ReadOnlySpan<byte> b) { }
        public void StageMutation(Fdp.Core.Entity e, Type t, object v) { }
        public Hrot.Diagnostics.Breakpoints.BreakpointId Add(
            Hrot.Diagnostics.Breakpoints.Breakpoint breakpoint) => default;
        public Hrot.Diagnostics.Breakpoints.BreakpointId AddBreakpoint(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto condition, Fdp.Core.Entity? filter = null,
            int occurrenceThreshold = 1, string displayName = "", Guid? sourceElementId = null) => default;
        public void Remove(Hrot.Diagnostics.Breakpoints.BreakpointId id) { }
        public void SetEnabled(Hrot.Diagnostics.Breakpoints.BreakpointId id, bool enabled) { }
        public void UpdateCondition(Hrot.Diagnostics.Breakpoints.BreakpointId id,
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? condition) { }
        public void MarkAsWatch(Hrot.Diagnostics.Breakpoints.BreakpointId id, bool isWatch) { }
        public void SaveWatches(string path) { }
        public void LoadWatches(string path) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void OnHit(Hrot.Diagnostics.Breakpoints.Breakpoint bp, Fdp.Core.Entity entity) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void OnExternalHit(string tag, Fdp.Core.Entity entity) { }
        public event Action<Hrot.Diagnostics.Breakpoints.Breakpoint, Fdp.Core.Entity>? OnBreakpointHit
            { add { } remove { } }
        public event Action<bool>? OnPauseStateChanged { add { } remove { } }
        public bool IsPaused => false;
        public Fdp.ModuleHost.Abstractions.ISimulationView ActiveView => null!;
        public long PausedTick => 0;
        public int PendingMutationsCount => 0;
        public IReadOnlyList<Hrot.Diagnostics.Breakpoints.Breakpoint> AllBreakpoints
            => Array.Empty<Hrot.Diagnostics.Breakpoints.Breakpoint>();
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public void EvaluateStatefulBreakpoints(Fdp.Core.EntityRepository repo) { }
        public IReadOnlyList<(Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint,
            Hrot.Diagnostics.Breakpoints.CompiledComponentPredicate Compiled)> MountedComponentPredicates
            => Array.Empty<(Hrot.Diagnostics.Breakpoints.Breakpoint,
                            Hrot.Diagnostics.Breakpoints.CompiledComponentPredicate)>();
        public IReadOnlyList<(Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint,
            Hrot.Diagnostics.Breakpoints.CompiledEventScanner Scanner)> MountedEventScanners
            => Array.Empty<(Hrot.Diagnostics.Breakpoints.Breakpoint,
                            Hrot.Diagnostics.Breakpoints.CompiledEventScanner)>();
    }

    /// <summary>⭐ <c>internal</c> since <c>L1</c> so the production-registry rail reuses the SAME
    /// fake — ⛔ a second one would be two answers to "what is a no-op refactor service" (ruling 9).</summary>
    internal sealed class NoRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o)
            => new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o)
            => new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
