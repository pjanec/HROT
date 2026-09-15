using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-505</c>/<c>BP-506</c> — the debug session file: WHERE it lives, and that the WATCH PINS
/// actually reach it.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5 · <c>DebugSessionPaths</c> *(the user's <c>2026-08-24</c>
/// ruling)* · <c>FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md</c>.
///
/// <para>⭐⭐ <b>The pin half is the SILENT-DEFAULT control</b> *(<c>.claude/CLAUDE.md</c>)*: it asserts on
/// the CONSTRUCTED FILE, ⛔ never on the call site's source. 📌 <c>DebugSessionPersistence.Save</c>'s
/// <c>pinnedVariables</c> is optional and the editor's only production caller did not pass it, so no pin
/// was ever written however complete the persistence layer looked.</para>
/// </summary>
public sealed class ThePinnedRowsReachTheSessionFileTests
{
    private static readonly Guid AssetA = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid AssetB = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    private static Entity Ent(int index) => new(index, 1);

    private static VariableRow Row(Guid asset, Entity entity, string name)
        => new(Origin:    new VariableRowOrigin(asset, entity, "s", name, "Alpha"),
               ShortName: name,
               TypeText:  "int",
               ClrType:   typeof(int),
               ReadValue: () => BitConverter.GetBytes(1));

    private sealed class NoTimeControl : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }

    private sealed class NoRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
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

    /// <summary>⭐ A registrar built the way the composition root builds one, so its Watch is real.</summary>
    private static PerspectiveWorkspaceRegistrar Registrar(string perspective)
    {
        var live    = new EntityRepository();
        var preTick = new EntityRepository();

        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new NoRefactor(), new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false)
        {
            // ⚠ Without one the registrar creates NO Watch at all, and this rail would assert nothing.
            BreakpointManager = new DataBreakpointManager(
                live, preTick, new DebugSnapshotProvider(preTick), new NoTimeControl()),
        };

        return services.CreateRegistrar(perspective, new EditorSelectionStore(),
                                        Array.Empty<IAssetValidator>());
    }

    private static string TempFile()
        => Path.Combine(Directory.CreateTempSubdirectory("hrot-bpsession-").FullName, "bpsession.json");

    // ══ BP-506 — the pins reach the file ════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE FORWARDING RAIL — asserted on the file <c>EditorSubsystem</c> actually writes.</b>
    ///
    /// <para>⛔ Reverting the <c>CapturePinnedVariables(registrars)</c> argument inside
    /// <c>WriteDebugSession</c> reddens this — that IS the defect, and it is what "a rail never seen red
    /// is decoration" means here.</para>
    ///
    /// <para>⭐ THREE perspectives, because each owns its own Watch and its own pin store: a gather that
    /// asked only the active one would pass a single-source rail and lose two thirds of the pins.</para>
    /// </summary>
    [Fact]
    public void EveryPerspectivesPinnedRowsAreWrittenToTheSessionFile()
    {
        var btree     = Registrar("BTree");
        var hsm       = Registrar("HSM");
        var blueprint = Registrar("Blueprint");

        btree.Watch!.Pinned.Pin(Row(AssetA, Ent(1), "Health"),
                                EntityBinding.Concrete(11, Ent(1)));
        hsm.Watch!.Pinned.Pin(Row(AssetB, Ent(2), "Ammo"),
                              EntityBinding.Concrete(22, Ent(2)));
        blueprint.Watch!.Pinned.Pin(Row(AssetA, default, "Speed"), EntityBinding.Chameleon);

        var path = TempFile();
        EditorSubsystem.WriteDebugSession(null, null, new[] { btree, hsm, blueprint }, path);

        var file = DebugSessionPersistence.TryLoad(path);
        Assert.NotNull(file);
        Assert.Equal(3, file!.PinnedVariables.Count);

        Assert.Contains(file.PinnedVariables,
                        e => e.VariablePath == "Health" && e.NetworkId == 11 && e.BindingKind == "Concrete");
        Assert.Contains(file.PinnedVariables,
                        e => e.VariablePath == "Ammo"   && e.NetworkId == 22 && e.BindingKind == "Concrete");
        Assert.Contains(file.PinnedVariables,
                        e => e.VariablePath == "Speed"  && e.NetworkId == 0  && e.BindingKind == "Chameleon");

        // ⭐ …and it round-trips back into bindings, which is what a restore will consume.
        var restored = PinnedVariablePersistence.Restore(file, out int skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(3, restored.Count);
    }

    /// <summary>
    /// ⚠ <b>A pin with no durable id is SKIPPED, not written as <c>NetworkId 0</c></b> — writing it would
    /// restore a pin pointing at nothing, which reads as data loss rather than as the within-session pin
    /// it always was. ⛔ The other perspectives' pins are unaffected.
    /// </summary>
    [Fact]
    public void AnUnpersistablePinIsSkippedWithoutLosingTheOthers()
    {
        var btree = Registrar("BTree");
        btree.Watch!.Pinned.Pin(Row(AssetA, Ent(1), "Health"), EntityBinding.Concrete(11, Ent(1)));
        // ⛔ NetworkId 0 on a CONCRETE binding — an editor-only entity.
        btree.Watch!.Pinned.Pin(Row(AssetA, Ent(9), "EditorOnly"), EntityBinding.Concrete(0, Ent(9)));

        var path = TempFile();
        EditorSubsystem.WriteDebugSession(null, null, new[] { btree }, path);

        var file = DebugSessionPersistence.TryLoad(path)!;
        Assert.Equal("Health", Assert.Single(file.PinnedVariables).VariablePath);
    }

    /// <summary>⭐ A perspective with no Watch — a host built without a breakpoint manager — contributes
    /// nothing and throws nothing. ⛔ <c>null</c> registrars are the shape before <c>Initialize</c>.</summary>
    [Fact]
    public void AHostWithNoWatchContributesNothing()
    {
        var path = TempFile();
        EditorSubsystem.WriteDebugSession(null, null, new PerspectiveWorkspaceRegistrar?[] { null }, path);

        Assert.Empty(DebugSessionPersistence.TryLoad(path)!.PinnedVariables);
    }

    // ══ BP-505 — where the file lives, and the git-curated reset ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RESET</b> — 🔒 the user's <c>2026-08-24</c> ruling: <i>"always overwrite the user's copy
    /// with git maintained curated copy on start."</i> ⛔ Whatever the user's copy held is GONE.
    /// </summary>
    [Fact]
    public void TheCuratedCopyForceOverwritesTheUsersCopy()
    {
        var curated = Directory.CreateTempSubdirectory("hrot-curated-").FullName;
        var user    = Directory.CreateTempSubdirectory("hrot-user-").FullName;

        File.WriteAllText(Path.Combine(curated, DebugSessionPaths.FileName), "{\"Watches\":[]}");
        File.WriteAllText(Path.Combine(user,    DebugSessionPaths.FileName), "POISONED");

        Assert.True(DebugSessionPaths.TryResetUserSessionFrom(curated, user));
        Assert.Equal("{\"Watches\":[]}", File.ReadAllText(DebugSessionPaths.UserPath(user)));
    }

    /// <summary>
    /// ⚠ <b>No curated copy shipped ⇒ the user's copy is UNTOUCHED and the answer is <c>false</c></b> —
    /// ⛔ not an exception, and ⛔ not a deleted session. 📌 A deployed build that ships no curated file is
    /// a legitimate configuration, exactly as <c>LayoutPaths.TryResetUserLayout</c> treats it.
    /// </summary>
    [Fact]
    public void WithoutACuratedCopyTheUsersSessionSurvives()
    {
        var curated = Directory.CreateTempSubdirectory("hrot-curated-empty-").FullName;
        var user    = Directory.CreateTempSubdirectory("hrot-user-keep-").FullName;
        File.WriteAllText(Path.Combine(user, DebugSessionPaths.FileName), "MINE");

        Assert.False(DebugSessionPaths.TryResetUserSessionFrom(curated, user));
        Assert.Equal("MINE", File.ReadAllText(DebugSessionPaths.UserPath(user)));
    }

    /// <summary>
    /// ⭐⭐ <b>The curated file is COMMITTED and PARSES.</b> 🔴 It is the file every development start is
    /// reset to — 📌 <c>FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md</c>: an unparseable session file
    /// killed the editor on every launch. ⛔ A clean environment that bricks the editor is worse than none.
    /// </summary>
    [Fact]
    public void TheCommittedCuratedSessionIsCleanAndLoadable()
    {
        var repoCurated = SourceCuratedFile();
        Assert.True(File.Exists(repoCurated), $"the committed curated session is missing: {repoCurated}");

        var file = DebugSessionPersistence.TryLoad(repoCurated);
        Assert.NotNull(file);
        Assert.Empty(file!.NodeBreakpoints);
        Assert.Empty(file.DataBreakpoints);
        Assert.Empty(file.Watches);
        Assert.Empty(file.PinnedVariables);
    }

    /// <summary>⭐ Walks up to <c>&lt;repo&gt;/debug/default/bpsession.json</c> — the SOURCE side of the
    /// build's <c>Content Link</c>, so the rail checks what git holds rather than what a stale output
    /// directory happens to carry.</summary>
    private static string SourceCuratedFile()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "debug", "default", DebugSessionPaths.FileName);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine("debug", "default", DebugSessionPaths.FileName);   // ⛔ fails the Exists assert
    }
}
