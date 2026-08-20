using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Variables;
using NodeEditor.Core.Action;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 98 (<c>98c</c>) — the outline's "Watch this variable" actually pins (<c>BP-360</c>).</b>
///
/// <para>🔴 <b>The defect.</b> 📐 <c>MyBlueprintContextMenu:40</c> enables the entry on
/// <c>commands.Get("editor.toggle-variable-watch") is not null</c>, and 📐 <b>nothing in the repo
/// registered that id</b> — the only other mention was a test asserting the constant. ⇒ Batch 94's
/// <i>"ONE command, TWO entry points"</i> shipped with <b>one</b>: the Details table's entry was wired
/// by the registrar, the outline's was drawn and permanently greyed.</para>
///
/// <para>⭐⭐⭐ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: the <b>DRAW</b> layer, and only that
/// — 📌 <c>R-21</c>/<c>R-62</c>, no headless rail can drive ImGui, so the menu's own greying is
/// unrailed as always. ⭐ Everything below is real: the real <see cref="EditorSubsystem"/> composition
/// root, its real registrar and Watch store, the real outline window and a real
/// <see cref="BlueprintAsset"/>. ⛔ <b>The assertion is that the row is PINNED</b>, not that a command
/// exists.</para>
/// </summary>
public sealed class TheOutlineWatchEntryIsLiveTests : IDisposable
{
    private readonly IconAtlas _atlas = new(IntPtr.Zero, 16f, 16f);
    public void Dispose() => _atlas.Dispose();

    /// <summary>
    /// ⭐⭐⭐ <b>THE agreement rail.</b> Three places spell this id and none of them may drift:
    /// <c>VariableWatchGesture.CommandId</c> *(the gesture)*, <c>MyBlueprintContextMenu</c>'s literal
    /// *(the menu, deliberately dependency-free)* and the registration <c>98c</c> adds.
    ///
    /// <para>⚠ <b>A drift here does not fail loudly</b> — it silently returns the entry to being
    /// permanently greyed, which is exactly how <c>BP-360</c> survived two batches. ⭐ That is why the
    /// agreement is asserted rather than trusted to a shared constant the menu is forbidden to import.</para>
    /// </summary>
    [Fact]
    public void TheThreeSpellingsOfTheCommandIdAgree()
    {
        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterToggleVariableWatchCommand(commands, _ => { });

        Assert.NotNull(commands.Get(VariableWatchGesture.CommandId));
        // ⭐ The menu's own literal, restated here — ⛔ NOT imported, because the menu deliberately does
        //   not depend on the variable assembly and this rail must fail if either side moves.
        Assert.NotNull(commands.Get("editor.toggle-variable-watch"));
    }

    /// <summary>
    /// 🔴🔴 <b>RED before <c>98c</c>: no host registered the command, so the entry was greyed.</b>
    /// ⭐ Through the REAL composition root, and the assertion is the Watch store's contents.
    /// </summary>
    [Fact]
    public void TheOutlineEntry_PinsTheVariable()
    {
        var s = Scene();

        Assert.Empty(s.Watch.Pinned.GetRows());
        s.Commands.Invoke(VariableWatchGesture.CommandId, Ctx(s.ItemId));

        var pinned = Assert.Single(s.Watch.Pinned.GetRows());
        Assert.Equal("Count", pinned.ShortName);
    }

    /// <summary>
    /// ⭐⭐ <b>It is a TOGGLE, and the two entry points share ONE store.</b> ⛔ A second invoke unpins —
    /// resolved against the store, never a remembered flag, so the Details table and the outline cannot
    /// disagree about what is pinned.
    /// </summary>
    [Fact]
    public void ASecondInvoke_Unpins()
    {
        var s = Scene();

        s.Commands.Invoke(VariableWatchGesture.CommandId, Ctx(s.ItemId));
        s.Commands.Invoke(VariableWatchGesture.CommandId, Ctx(s.ItemId));

        Assert.Empty(s.Watch.Pinned.GetRows());
    }

    /// <summary>
    /// ⛔ <b>An id that names no variable REFUSES, and says so.</b> 📌 <c>BP-223</c>/<c>Q26-B2</c>: a
    /// gesture that cannot proceed must SAY so — ⛔ never a silent return, and ⛔ never a pinned guess.
    /// </summary>
    [Fact]
    public void AnItemThatIsNotAVariable_RefusesAndExplains()
    {
        var s = Scene();

        s.Commands.Invoke(VariableWatchGesture.CommandId, Ctx("graph:not-a-variable"));

        Assert.Empty(s.Watch.Pinned.GetRows());
        Assert.False(string.IsNullOrEmpty(s.Window.LastRefusal),
            "a refusal that explains nothing is the outcome Q26-B2 rules out");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The toggle reaches the window through the registrar's ONE extra-window pass</b> —
    /// 📌 <c>R-67</c>, the same route the live projection takes. ⛔ Asserted on the CONSTRUCTED window,
    /// never on the registrar's source.
    /// </summary>
    [Fact]
    public void TheRegistrarInstallsTheToggle_OnTheConstructedWindow()
        => Assert.True(Scene().Window.HasWatchToggle);

    // ── the harness ─────────────────────────────────────────────────────────

    private sealed record Rig(
        BlueprintMyBlueprintWindow Window, EditorCommandsImpl Commands,
        Hrot.Editor.AiShared.Windows.AiWatchWindow Watch, string ItemId);

    /// <summary>
    /// ⭐ The real registrar, its real Watch and its real <c>RegisterExtraWindow</c> pass.
    ///
    /// <para>⚠⚠ <b>What is faked, stated plainly *(<c>M-29</c>)*: the registrar's CONSTRUCTION.</b>
    /// 📐 Measured while writing this: a headless <see cref="EditorSubsystem"/> has
    /// <c>registrar.Watch == null</c>, because the Watch window is only built when a
    /// <c>IDataBreakpointManager</c> was supplied and the subsystem sets its own in <c>Initialize</c>,
    /// which needs a running host. ⇒ this builds the services bundle itself, with a manager.
    /// ⛔ 📌 <c>R-67</c> — <b>a rail that builds its own composition root cannot see a composition-root
    /// defect</b>; ⭐ what it CAN see, and does, is that the registrar's own extra-window pass installs
    /// the toggle and that the command then pins a real row.</para>
    /// </summary>
    private Rig Scene()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "WatchHost",
            Dispatch = BlueprintDispatchKind.AiPrimitive,
            Graphs   = new List<Graph>(),
            Header   = new Header(),
        };
        BlueprintDocumentFactory.CreateVariable(asset, "Count", "System.Int32");

        var services = new Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            new NoRefactorForWatch(),
            new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp:  () => false,
            isFrozen: () => false)
        {
            // ⭐ The Watch exists exactly when a breakpoint manager does — the registrar's own rule,
            //   ⛔ not one this rail invents. Reuses the SAME recorder Batch 97 promoted (ruling 9).
            BreakpointManager = new Hrot.Blueprints.Tests.Debug
                                    .TheSessionWritesWhileFrozenTests.RecordingManager(),
        };

        var registrar = services.CreateRegistrar(
            "Blueprint", new Hrot.Editor.AiShared.Selection.EditorSelectionStore(),
            validators: Array.Empty<Hrot.Editor.AiShared.Validation.IAssetValidator>());

        Assert.NotNull(registrar.Watch);

        var window = new BlueprintMyBlueprintWindow();
        // ⭐ The registrar's OWN extra-window pass — the production route, ⛔ not a hand-set field.
        registrar.RegisterExtraWindow(new WindowManager(_atlas), window);

        var commands = new EditorCommandsImpl();
        window.Retarget(null, asset, null, commands, null, () => Guid.Empty);

        // ⭐ The id the OUTLINE itself produces — ⛔ not fabricated, so a change to the item-id scheme
        //   reddens this rail rather than leaving the command matching nothing.
        var itemId = window.RowIdForTest("Count");

        return new Rig(window, commands, registrar.Watch!, itemId);
    }

    /// <summary>⭐ Nothing here exercises refactoring.</summary>
    private sealed class NoRefactorForWatch : Hrot.Editor.AiShared.Refactor.IRefactorService
    {
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public Hrot.Editor.AiShared.Refactor.RefactorPreview PreviewRename(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o)
            => new(f, t, Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorFileEdit>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyRename(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public Hrot.Editor.AiShared.Refactor.DeletePreview PreviewDelete(
            Guid id, Hrot.Editor.AiShared.Refactor.DeleteOptions o)
            => new(id, Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyDelete(
            Hrot.Editor.AiShared.Refactor.DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorPreview> PreviewRenameAsync(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o,
            System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorResult> ApplyRenameAsync(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }

    private static EditorCommandContext Ctx(string itemId)
        => new(ScreenPos: null, CanvasPos: null,
               Args: new Dictionary<string, object?> { ["itemId"] = itemId });
}
