using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Memory;
using StructEdit.Core.UnionSupport;
using StructEdit.Reflection;

namespace StructEdit.Tests.Session;

// ── Test fixtures ─────────────────────────────────────────────────────────────

file enum PayloadMode { Ballistic = 0, Guided = 1 }

file struct BallisticPayload
{
    public float Speed;
    public float Mass;
}

file struct GuidedPayload
{
    public float Speed;
    public float TargetX;
}

// Layout: Mode(int=4 bytes at offset 0), Payload(fixed byte[8] at offset 4)
file unsafe struct ProjectileComponent
{
    public PayloadMode Mode;
    public fixed byte Payload[8];
}

file sealed class ProjectilePayloadViewProvider : IBufferViewProvider
{
    public bool CanCreateView(BufferViewRequest request)
        => request.ComponentType == typeof(ProjectileComponent)
           && request.BufferPath.Value == "$.Payload";

    public BufferViewResult CreateView(BufferViewRequest request)
    {
        var mode = request.ReadSibling<PayloadMode>(EditPath.Parse("$.Mode"));
        return mode == PayloadMode.Guided
            ? request.ProjectBufferAs(typeof(GuidedPayload), "GuidedPayload")
            : request.ProjectBufferAs(typeof(BallisticPayload), "BallisticPayload");
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class UnionHelper
{
    public static IComponentEditService BuildService()
        => new ComponentEditServiceBuilder()
            .RegisterBufferViewProvider(new ProjectilePayloadViewProvider())
            .Build();

    public static unsafe ProjectileComponent MakeComponent(PayloadMode mode = PayloadMode.Ballistic)
        => new() { Mode = mode };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class UnionTests
{
    // R004-T1: Provider creates view — document has BufferView node for Payload
    [Fact]
    public void ProviderCreatesView_DocumentHasBufferViewNode()
    {
        var service = UnionHelper.BuildService();
        using var session = service.Open(
            UnionHelper.MakeComponent(PayloadMode.Ballistic),
            typeof(ProjectileComponent));

        var root = session.Document.Root;
        // Provider replaces the raw FixedBuffer node; search by kind
        var payloadNode = root.Children.FirstOrDefault(c => c.Kind == EditNodeKind.BufferView);

        payloadNode.Should().NotBeNull("a BufferView node must replace the Payload fixed buffer");
        payloadNode!.Name.Should().Be("BallisticPayload");
        payloadNode.Children.Should().NotBeEmpty("provider must produce child nodes for BallisticPayload fields");
        payloadNode.Children.Select(c => c.Name).Should().Contain(new[] { "Speed", "Mass" });
    }

    // R004-T2: MarkStructuralChange sets RebuildRequired
    [Fact]
    public void MarkStructuralChange_SetsRebuildRequired()
    {
        var service = UnionHelper.BuildService();
        using var session = service.Open(
            UnionHelper.MakeComponent(),
            typeof(ProjectileComponent));

        session.RebuildState.Should().Be(EditRebuildState.Stable);
        session.MarkStructuralChange();
        session.RebuildState.Should().Be(EditRebuildState.RebuildRequired);
    }

    // R004-T3: RebuildDocument preserves dirty flag when buffer was written to
    [Fact]
    public void RebuildDocument_PreservesIsDirty()
    {
        var service = UnionHelper.BuildService();
        using var session = service.Open(
            UnionHelper.MakeComponent(),
            typeof(ProjectileComponent));

        // Write to Mode binding to make buffer dirty
        var modeNode = session.Document.Root.Children.First(c => c.Name == "Mode");
        modeNode.Binding!.SetBoxed(PayloadMode.Guided);
        session.IsDirty.Should().BeTrue("writing via binding must mark buffer dirty");

        session.MarkStructuralChange();
        session.RebuildDocument();

        session.IsDirty.Should().BeTrue("rebuild must not clear dirty flag");
        session.RebuildState.Should().Be(EditRebuildState.Stable);
    }

    // R004-T4: RebuildDocument produces new view after discriminator change
    [Fact]
    public void RebuildDocument_ProducesNewViewAfterModeChange()
    {
        var service = UnionHelper.BuildService();
        using var session = service.Open(
            UnionHelper.MakeComponent(PayloadMode.Ballistic),
            typeof(ProjectileComponent));

        // Verify initial view is BallisticPayload
        var payload = session.Document.Root.Children.First(c => c.Kind == EditNodeKind.BufferView);
        payload.Name.Should().Be("BallisticPayload");

        // Change discriminator (Mode) to Guided
        var modeNode = session.Document.Root.Children.First(c => c.Name == "Mode");
        modeNode.Binding!.SetBoxed(PayloadMode.Guided);

        session.MarkStructuralChange();
        session.RebuildDocument();

        // New document should show GuidedPayload
        var newPayload = session.Document.Root.Children.First(c => c.Kind == EditNodeKind.BufferView);
        newPayload.Kind.Should().Be(EditNodeKind.BufferView);
        newPayload.Name.Should().Be("GuidedPayload");
        newPayload.Children.Select(c => c.Name).Should().Contain(new[] { "Speed", "TargetX" });
    }
}
