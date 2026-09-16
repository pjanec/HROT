using System;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Hsm.Editor.Windows;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>The <c>R-67</c> half for the HSM events Details view.</b>
/// 🔒 User ruling, <c>2026-08-23</c>: <i>"the hsm event one is a good candidate for details panel view
/// if hsm details panel."</i>
///
/// <para>⛔⛔ <b>Why this rail exists and why it is the ONLY one that can catch this defect.</b>
/// <see cref="HsmEventsDetailsView"/>'s own unit rails prove the VIEW works — its predicate, its
/// rebuild-on-asset-change, its snapshot dump. ⚠ <b>Every one of them passes on a view nothing ever
/// registers.</b> 📌 That is <c>BP-327</c>'s shape, the defect this programme keeps re-finding: a
/// capability BUILT AND UNREACHABLE. ⇒ ⭐ only an assertion over the PRODUCTION composition root can
/// see it, which is exactly the split
/// <c>TheScenarioComponentsViewTests.TheScenarioCatalogue_OffersTheComponentsView</c> already
/// established for the same reference wall.</para>
///
/// <para>⚠ <b>The registration happens in <c>EditorSubsystem</c> and nowhere else, BY CONSTRUCTION</b>
/// — <c>HsmEventsDetailsView</c> lives in <c>Hrot.Hsm.Editor</c>, while <c>IDetailsViewInstance</c> and
/// <c>PerspectiveWorkspaceRegistrar</c> live in <c>Hrot.Editor.AiShared</c> BELOW it, and AiShared does
/// not reference Hsm.Editor. ⛔ So this cannot ride the usual claim chain; the root is the only
/// assembly that sees both ends.</para>
/// </summary>
public sealed class TheHsmEventsViewIsRegisteredTests
{
    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    /// <summary>⭐⭐ The HSM perspective's Details catalogue offers the events view.</summary>
    [Fact]
    public void TheHsmCatalogue_OffersTheEventsView()
    {
        var registrar = RealEditor().RegistrarFor("HSM");
        Assert.NotNull(registrar);
        Assert.Contains(HsmEventsDetailsViewDescriptor.ViewId,
                        registrar!.DetailsViews.All.Select(d => d.Id));
    }

    /// <summary>
    /// ⚠⚠ <b>The negative half — and it is not decoration.</b> ⭐ The view is ASSET-scoped to
    /// <c>HsmAsset</c>, so offering it on the BTree perspective would be a chameleon: the right view,
    /// drawn about the wrong host. ⛔ A single misplaced <c>.Add</c> line produces exactly that, and
    /// the positive rail above would still be green.
    /// </summary>
    [Fact]
    public void TheBTreeCatalogue_DoesNotOfferIt()
    {
        var registrar = RealEditor().RegistrarFor("BTree");
        Assert.NotNull(registrar);
        Assert.DoesNotContain(HsmEventsDetailsViewDescriptor.ViewId,
                              registrar!.DetailsViews.All.Select(d => d.Id));
    }
}
