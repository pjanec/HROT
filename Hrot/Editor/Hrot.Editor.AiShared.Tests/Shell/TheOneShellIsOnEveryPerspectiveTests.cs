using System;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>S1</c>'s STAGE GATE (<c>BP-399</c>) — ONE SHELL CLASS, ON EVERY PERSPECTIVE.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3 ①③④ ·
/// 📄 <c>TASKS_One_Shell_BP399.md</c> §2's gate rows ② and ③.
///
/// <para>🔒 <b>The user's ask, verbatim (<c>2026-08-22</c>):</b> <i>"Visually we have one Details window
/// in Scenario/HSM/Btree/Blueprint perspectives … This is what I call a shell and this needs to be
/// same/reused across the perspectives, no parallel implementations."</i></para>
///
/// <para>🔴 <b>What was true before <c>S1</c>:</b> <c>PerspectiveWorkspaceRegistrar</c> built the shell
/// inside <c>if (HostKindOf(perspective) != null)</c>, which answers only BTree and HSM. ⇒ Blueprint
/// got no shell from the registrar and <c>BlueprintDetailsWindow</c> — a separate sealed class with
/// <b>no view registry, no toolbar and no float/pin</b> — filled the slot under the SAME id and title.
/// ⛔ 📄 §7.3 ③: <i>"`HostKindOf` answers 'which blackboard host is this?' … reusing it as the shell gate
/// is the actual bug."</i></para>
///
/// <para>⚠ <b>What these rails do NOT prove</b> *(📌 <c>R-21</c>/<c>R-62</c>)*: that a toolbar button
/// is visible on screen. ⭐ They prove the MODEL says it should be — which is the half a headless run
/// can own; ⛔ the pixels stay with the user's visual check *(<c>R-27</c>)</para>
/// </summary>
public sealed class TheOneShellIsOnEveryPerspectiveTests
{
    private static PerspectiveWorkspaceRegistrar Production(string perspective)
    {
        var services = new PerspectiveWorkspaceServices(
            // ⭐ Reuses the layout rail's fakes — ⛔ not a second set (ruling 9).
            new AssetCatalog(), new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false);

        return services.CreateRegistrar(
            perspective, new EditorSelectionStore(),
            validators: Array.Empty<IAssetValidator>());
    }

    /// <summary>⭐ The three AI perspectives. ⚠ Scenario is built at the composition root, not here —
    /// <c>TheFloatAndPinEntryPointsAreReachableTests</c> covers that path.</summary>
    public static TheoryData<string> AiPerspectives => new() { "BTree", "HSM", "Blueprint" };

    // ══ gate ② — the SAME CLASS, and the id is unchanged ═════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Gate ②:</b> every AI perspective's Details panel is the shared
    /// <see cref="DetailsWindow"/> — ⛔ not a per-host class.
    /// <para>⚠ <b>Asserted as an EXACT type</b>, not <c>is</c>: a subclass would be a parallel
    /// implementation wearing the shell's name, which is precisely what §7.3 ① forbids.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AiPerspectives))]
    public void EveryAiPerspectivesDetailsPanel_IsTheOneShellClass(string perspective)
    {
        var details = Production(perspective).Details;

        Assert.NotNull(details);
        Assert.Equal(typeof(DetailsWindow), details!.GetType());
        Assert.Equal("Details", details.Title);
    }

    /// <summary>
    /// ⭐⭐ <b>Gate ② — and the PERSISTED ids are unchanged.</b> 📄 §7.3 ④ / §5: a bare key rename
    /// <i>"silently resets layouts"</i>. ⛔ The TYPE changed; the KEY must not.
    /// </summary>
    [Theory]
    [InlineData("BTree",     "ai_details_btree")]
    [InlineData("HSM",       "ai_details_hsm")]
    [InlineData("Blueprint", "ai_details_blueprint")]
    public void TheShellKeepsThePersistedWindowId(string perspective, string expectedId)
        => Assert.Equal(expectedId, Production(perspective).Details!.Id);

    // ══ gate ③ — Blueprint gains float and pin ═══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Gate ③: with a document open, every AI perspective's shell offers FLOAT and PIN.</b>
    ///
    /// <para>🔴 <b>The half that was missing on Blueprint</b> — <c>BlueprintDetailsWindow</c> had no
    /// <c>OpenFloat</c>, no <c>Pin</c> and no toolbar at all, so the user's <c>2026-08-22</c> report
    /// *("the buttons for pinned and unpinned floating windows work!")* was true of BTree and HSM and
    /// false of Blueprint.</para>
    ///
    /// <para>⭐⭐ <b>Both halves are required and both are asserted:</b> a REGISTERED shell
    /// *(which is what hands it a <c>WindowManager</c> — <c>OnRegistered</c>, <c>R-126</c>'s pull)*, and
    /// a view that CLAIMS the context. ⚠ The open document is what makes <c>details.blackboard</c>
    /// apply *(predicate <c>HasAsset</c>)</para>
    ///
    /// <para>⛔ <b>The negative half is in the same test, deliberately:</b> before the asset is set no
    /// view claims the panel, so <c>ShowsFloatAndPin</c> must be <b>false</b> — ⚠ without that this
    /// would pass against a property hard-wired to <c>true</c>.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AiPerspectives))]
    public void WithADocumentOpen_EveryAiShellOffersFloatAndPin(string perspective)
    {
        var registrar = Production(perspective);
        var windows   = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        registrar.RegisterWindows(windows);

        var shell = registrar.Details!;

        // ⛔ Nothing open ⇒ nothing claims the panel ⇒ nothing to float (R-117's grey line).
        Assert.False(shell.ShowsFloatAndPin,
            "with no document open no view claims the panel, so there is nothing to float.");

        registrar.SelectionStore.ActiveAsset = new OpenDocument();

        Assert.True(shell.ShowsFloatAndPin,
            $"'{perspective}' has a document open and a view showing, but its shell offers no float "
          + "or pin — §7.3 ① gives every perspective the same shell, and that is what it is for.");
    }

    /// <summary>
    /// ⭐⭐ <b>And the gesture actually produces a window</b>, not just a <c>true</c>.
    /// ⚠ 📌 <c>BP-402</c> ①: a decision property that nothing acts on is a rail that reddens for the
    /// wrong reason. ⭐ This asks the <c>WindowManager</c>, which is where a float has to end up.
    /// </summary>
    [Theory]
    [MemberData(nameof(AiPerspectives))]
    public void FloatingFromAnAiShell_RegistersAWindow(string perspective)
    {
        var registrar = Production(perspective);
        var windows   = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        registrar.RegisterWindows(windows);
        registrar.SelectionStore.ActiveAsset = new OpenDocument();

        var shell = registrar.Details!;
        var floated = shell.OpenFloat(windows);

        Assert.NotNull(floated);
        Assert.Contains(windows.RegisteredWindowIds, id => id == floated!.Id);
    }

    /// <summary>⭐ The minimum an open document has to be for <c>HasAsset</c> to hold.</summary>
    private sealed class OpenDocument : IEditableAsset
    {
        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "OpenDocument";
        public AssetKind Kind           => AssetKind.Blueprint;
        public string    SourceFilePath => "/open.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed { add { } remove { } }
    }
}
