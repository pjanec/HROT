using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Common;
using Hrot.Editor;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// ⭐⭐⭐ The EQS Brain authoring — the Blueprint node drawers and the SpawnEqsSensor template picker —
/// exists on the CONSTRUCTED editor AND the constructed CGF, built by the same binder, listing the same
/// templates. 📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.8.
/// <para>🔴 Before §17.8 CGF built NO node drawers and installed no Blueprint Details node view, so a
/// blueprint opened on CGF had no node Details and no EQS template could be picked there.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsAuthoringOnBothHostsTests
{
    private static WindowManager MakeWindowManager() => new(new IconAtlas(System.IntPtr.Zero, 512, 512));

    [Fact(Timeout = 60_000)]
    public void BothHostsBuildTheSameBlueprintNodeAuthoring_WithTheAreaTemplate()
    {
        _ = typeof(Hrot.SimHost.Systems.EntitiesOfForceInArea);
        var areaId = new System.Guid(Hrot.SimHost.Systems.EntitiesOfForceInArea.AssetId);

        var editor = new EditorSubsystem();
        using var cgf = new CgfHarness();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            editor.RegisterWindows(MakeWindowManager());
            cgf.CgfSvc.RegisterWindows(MakeWindowManager());

            var onEditor = editor.BlueprintNodeAuthoringForTest;
            var onCgf    = cgf.CgfSvc.BlueprintNodeAuthoringForTest;
            Assert.NotNull(onEditor);
            Assert.NotNull(onCgf);

            foreach (var authoring in new[] { onEditor!, onCgf! })
            {
                Assert.True(authoring.Drawers.TryGet(typeof(SpawnEqsSensorNode), out var drawer) && drawer != null,
                    "the SpawnEqsSensor drawer (the template picker) must be registered");
                Assert.NotNull(authoring.EqsTemplates.TryGet(areaId));
            }

            Assert.Equal(
                onEditor!.EqsTemplates.EnumerateAll().Select(t => t.AssetId).OrderBy(g => g),
                onCgf!.EqsTemplates.EnumerateAll().Select(t => t.AssetId).OrderBy(g => g));
        }
        finally
        {
            editor.Shutdown();
        }
    }
}
