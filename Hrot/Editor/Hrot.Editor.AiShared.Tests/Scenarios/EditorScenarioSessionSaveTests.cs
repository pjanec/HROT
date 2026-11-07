using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Scenarios;

/// <summary>
/// ⭐⭐⭐ CE-275 ③ — the editor (and CGF, via this same class) SAVES THROUGH THE CLUSTER, never with a local
/// direct file write. Proves <see cref="EditorScenarioSession.SaveAs"/> / <see cref="EditorScenarioSession.SaveCurrent"/>
/// publish a <see cref="StorageOpType.SaveScenario"/> storage-op intent carrying a scenario NAME (not a
/// path) and touch no filesystem. 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
/// </summary>
public sealed class EditorScenarioSessionSaveTests
{
    private static readonly string FakeRoot = Path.Combine(Path.GetTempPath(), "hrot-scnroot-should-not-be-written");

    private static EditorScenarioSession NewSession(FdpEventBus bus)
    {
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        return new EditorScenarioSession(fileService, bus, () => new Fdp.Toolkit.Orchestration.ClusterView(bus), new EntityRepository(), () => FakeRoot);
    }

    /// <summary>
    /// ⭐ CE-3394 (presets D10) — a world built with a variant ≠ 0 is NOT saved: <see cref="IScenarioSession.SaveDisabledReason"/> is the gate's reason (naming the variant; the File menu
    /// greys Save / Save As with it) and SaveAs / SaveCurrent publish nothing; an as-authored world saves as before.
    /// 🔴 Red-proof (compiles): <c>SaveDisabledReason</c> answering null ⇒ the intent is published ⇒ red.
    /// </summary>
    [Fact]
    public void AVariantWorld_IsNotSaved_AndTheReasonNamesTheVariant()
    {
        var bus = new FdpEventBus();
        var world = new EntityRepository();
        var fileService = new ScenarioFileService(new ScenarioSerializerBuilder("Hrot.Scenario").Build());
        var session = new EditorScenarioSession(fileService, bus, () => new Fdp.Toolkit.Orchestration.ClusterView(bus), world, () => FakeRoot);
        Assert.Null(((IScenarioSession)session).SaveDisabledReason);

        world.RegisterManagedComponent<VariantForms>();
        world.SetManagedComponent(world.CreateEntity(), new VariantForms { Variant = 5 });
        world.Tick();   // a load moves the world's version (the reason is cached per version)
        Assert.Contains("variant 5", ((IScenarioSession)session).SaveDisabledReason);
        session.SaveAs("area/variant_copy");
        bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<ExecuteStorageOpIntent>());
    }

    [Fact]
    public void SaveAs_PublishesSaveScenarioJsonIntent_WithTheName_AndWritesNoFile()
    {
        if (Directory.Exists(FakeRoot)) Directory.Delete(FakeRoot, recursive: true);

        var bus     = new FdpEventBus();
        var session = NewSession(bus);

        session.SaveAs("area/my_scenario");
        bus.SwapBuffers();

        var intent = Assert.Single(bus.ReadManaged<ExecuteStorageOpIntent>());
        Assert.Equal(StorageOpType.SaveScenario, intent.Operation);
        Assert.Equal("area/my_scenario", intent.ScenarioName);

        // ⛔ No local/direct write: the scenarios root must be untouched — the cluster handler writes, not this.
        Assert.False(Directory.Exists(FakeRoot));
    }

    [Fact]
    public void SaveCurrent_PublishesForTheLoadedName()
    {
        var bus     = new FdpEventBus();
        var session = NewSession(bus);

        session.SaveAs("keeper");   // sets the loaded name
        bus.SwapBuffers();
        _ = bus.ReadManaged<ExecuteStorageOpIntent>().ToArray();   // drain

        session.SaveCurrent();
        bus.SwapBuffers();

        var intent = Assert.Single(bus.ReadManaged<ExecuteStorageOpIntent>());
        Assert.Equal(StorageOpType.SaveScenario, intent.Operation);
        Assert.Equal("keeper", intent.ScenarioName);
    }

    [Fact]
    public void SaveCurrent_WithNoLoadedScenario_PublishesNothing()
    {
        var bus     = new FdpEventBus();
        var session = NewSession(bus);

        session.SaveCurrent();
        bus.SwapBuffers();

        Assert.Empty(bus.ReadManaged<ExecuteStorageOpIntent>());
    }
}
