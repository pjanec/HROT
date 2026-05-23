using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using NodeEditor.Core.Canvas;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmRendererRegistrationTests
{
    // ---- helper ----

    private static HsmAsset MakeDummyAsset()
    {
        var builder = new HsmBuilder("Dummy");
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), "Dummy", "", false, "");
    }

    // ---- tests ----

    [Fact]
    public void TransitionLabelRenderer_Id_equals_expected()
    {
        var renderer = new HsmTransitionLabelRenderer(MakeDummyAsset());
        renderer.Id.Should().Be("hsm.transition_labels");
    }

    [Fact]
    public void TransitionLabelRenderer_Pass_is_AfterWires()
    {
        var renderer = new HsmTransitionLabelRenderer(MakeDummyAsset());
        renderer.Pass.Should().Be(CanvasRenderPass.AfterWires);
    }

    [Fact]
    public void InitialArrowRenderer_Id_equals_expected()
    {
        var renderer = new HsmInitialArrowRenderer(MakeDummyAsset());
        renderer.Id.Should().Be("hsm.initial_state_arrows");
    }

    [Fact]
    public void InitialArrowRenderer_Pass_is_AfterNodes()
    {
        var renderer = new HsmInitialArrowRenderer(MakeDummyAsset());
        renderer.Pass.Should().Be(CanvasRenderPass.AfterNodes);
    }
}
