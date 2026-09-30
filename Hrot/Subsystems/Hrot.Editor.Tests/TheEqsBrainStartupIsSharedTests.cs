using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiComposition;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ The EQS Brain part — and its startup — is ONE implementation on the editor AND CGF.
/// 🔒 User, <c>2026-09-30</c>: <i>"the EQS brain part must be a shared code including the startup code for
/// editor and CGF alike"</i> · <i>"CGF == editor in most features, unification and sharing desired"</i>.
/// 📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.8. The constructed-host half of this rail is
/// <c>EqsAuthoringOnBothHostsTests</c> (ClusterRunner integration).
/// </summary>
public sealed class TheEqsBrainStartupIsSharedTests
{
    // ══ ① both hosts call the SAME binder, and neither builds the pieces itself ══

    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostBuildsBlueprintNodeAuthoringOnlyThroughTheSharedBinder(string project, string file)
    {
        var text = HostSource.Read(project, file);

        Assert.Contains("AiBlueprintNodeAuthoringBinder.CreateDrawers(", text);
        Assert.Contains("AiBlueprintNodeAuthoringBinder.InstallDetails(", text);
        Assert.DoesNotContain("BlueprintEditorBootstrap.CreateNodeDrawerRegistry(", text);
        Assert.DoesNotContain("BlueprintDetailsContribution.InstallInto(", text);
        Assert.DoesNotContain("EqsTemplateRegistry.Discover(", text);
    }

    [Theory]
    [InlineData("Hrot", "Subsystems", "Hrot.SimHost", "SimHostCapabilities.cs")]
    [InlineData("Hrot", "Subsystems", "Hrot.Editor", "EditorCapabilities.cs")]
    [InlineData("Stride", "HrotStrideApp.Game", "StrideCapabilities.cs")]
    public void EveryEqsSolverHostStartsItThroughTheSharedStartup(params string[] path)
    {
        var text = HostSource.ReadRelative(path);

        Assert.Contains("EqsSolverStartup.Register(context)", text);
        Assert.DoesNotContain("new EqsModule()", text);
        Assert.DoesNotContain("EqsTemplateRegistry.InstallDefault(", text);
    }

    // ══ ② what the binder builds ══

    [Fact]
    public void TheSharedPickerListsEveryRuntimeTemplate()
    {
        _ = typeof(Hrot.SimHost.Systems.EntitiesOfForceInArea); // make sure the declaring assembly is loaded

        var picker = AiBlueprintNodeAuthoringBinder.CreateEqsTemplates();

        Assert.NotNull(picker.TryGet(new System.Guid(Hrot.SimHost.Systems.EntitiesOfForceInArea.AssetId)));
        Assert.NotNull(picker.TryGet(new System.Guid("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d"))); // FindCoverFromTarget
    }
}
