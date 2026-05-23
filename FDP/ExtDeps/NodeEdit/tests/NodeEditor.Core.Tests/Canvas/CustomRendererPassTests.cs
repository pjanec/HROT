using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using ImGuiNET;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Canvas;

// ── Stub implementations ──────────────────────────────────────────────────────

// Minimal implementation of ICanvasRenderContext for testing purposes.
file sealed class FakeRenderContext : ICanvasRenderContext
{
    public ImDrawListPtr DrawList => default;
    public ViewportState Viewport { get; } = new ViewportState();
    public CanvasRenderPass Pass { get; set; }
    public IEditorTheme Theme => throw new NotImplementedException();
    public IGraphModel Graph => throw new NotImplementedException();
    public SelectionState Selection { get; } = new SelectionState();
    public IReadOnlySet<NodeId> VisibleNodes => new HashSet<NodeId>();
    public IReadOnlySet<LinkId> VisibleLinks => new HashSet<LinkId>();
    public float Zoom => 1f;
    public bool IsLowZoom => false;
    public IDebugSession? DebugSession => null;
    public IDictionary<string, object?> FrameScratch { get; } = new Dictionary<string, object?>();

    public Vector2 CanvasToScreen(Vector2 p) => p;
    public Vector2 ScreenToCanvas(Vector2 p) => p;
    public RectF CanvasToScreen(RectF r) => r;
}

// A test renderer that records which pass it was invoked for, and how many times.
file sealed class RecordingRenderer : ICustomCanvasRenderer
{
    public string Id { get; }
    public CanvasRenderPass Pass { get; }
    public bool IsActive { get; set; } = true;
    public List<CanvasRenderPass> Invocations { get; } = new();

    public RecordingRenderer(string id, CanvasRenderPass pass)
    {
        Id   = id;
        Pass = pass;
    }

    public void Render(ICanvasRenderContext ctx)
    {
        Invocations.Add(ctx.Pass);
    }

    public void Dispose() { }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public sealed class CustomRendererPassTests
{
    // Helper: simulate the InvokeCustomRenderers logic used inside CanvasRenderer.
    private static void InvokePass(
        IReadOnlyList<ICustomCanvasRenderer> renderers,
        ICanvasRenderContext ctx,
        CanvasRenderPass pass)
    {
        ((FakeRenderContext)ctx).Pass = pass;
        foreach (var r in renderers)
        {
            if (r.Pass == pass && r.IsActive)
                r.Render(ctx);
        }
    }

    [Fact]
    public void Active_renderer_is_invoked_exactly_once_for_its_declared_pass()
    {
        var r   = new RecordingRenderer("r1", CanvasRenderPass.AfterWires);
        var ctx = new FakeRenderContext();
        var all = new List<ICustomCanvasRenderer> { r };

        foreach (CanvasRenderPass pass in Enum.GetValues<CanvasRenderPass>())
            InvokePass(all, ctx, pass);

        r.Invocations.Should().HaveCount(1)
            .And.ContainSingle(p => p == CanvasRenderPass.AfterWires);
    }

    [Fact]
    public void Inactive_renderer_is_never_invoked()
    {
        var r   = new RecordingRenderer("inactive", CanvasRenderPass.BeforeContent) { IsActive = false };
        var ctx = new FakeRenderContext();
        var all = new List<ICustomCanvasRenderer> { r };

        foreach (CanvasRenderPass pass in Enum.GetValues<CanvasRenderPass>())
            InvokePass(all, ctx, pass);

        r.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void Renderers_at_same_pass_are_invoked_in_registration_order()
    {
        var invocationOrder = new List<string>();
        var r1 = new RecordingRendererWithOrder("first",  CanvasRenderPass.AfterNodes, invocationOrder);
        var r2 = new RecordingRendererWithOrder("second", CanvasRenderPass.AfterNodes, invocationOrder);
        var r3 = new RecordingRendererWithOrder("third",  CanvasRenderPass.AfterNodes, invocationOrder);
        var ctx = new FakeRenderContext();
        var all = new List<ICustomCanvasRenderer> { r1, r2, r3 };

        InvokePass(all, ctx, CanvasRenderPass.AfterNodes);

        invocationOrder.Should().Equal("first", "second", "third");
    }

    [Fact]
    public void Renderers_at_different_passes_only_fire_at_their_own_pass()
    {
        var rBefore = new RecordingRenderer("before", CanvasRenderPass.BeforeContent);
        var rAfterW = new RecordingRenderer("afterW", CanvasRenderPass.AfterWires);
        var rAfterN = new RecordingRenderer("afterN", CanvasRenderPass.AfterNodes);
        var rTop    = new RecordingRenderer("top",    CanvasRenderPass.TopMost);
        var ctx = new FakeRenderContext();
        var all = new List<ICustomCanvasRenderer> { rBefore, rAfterW, rAfterN, rTop };

        foreach (CanvasRenderPass pass in Enum.GetValues<CanvasRenderPass>())
            InvokePass(all, ctx, pass);

        rBefore.Invocations.Should().ContainSingle(p => p == CanvasRenderPass.BeforeContent);
        rAfterW.Invocations.Should().ContainSingle(p => p == CanvasRenderPass.AfterWires);
        rAfterN.Invocations.Should().ContainSingle(p => p == CanvasRenderPass.AfterNodes);
        rTop.Invocations.Should().ContainSingle(p => p == CanvasRenderPass.TopMost);
    }

    [Fact]
    public void Pass_enum_values_are_ordered_by_z_depth()
    {
        // BeforeContent < AfterWires < AfterNodes < TopMost (ascending z-order).
        var values = Enum.GetValues<CanvasRenderPass>();
        values[0].Should().Be(CanvasRenderPass.BeforeContent);
        values[1].Should().Be(CanvasRenderPass.AfterWires);
        values[2].Should().Be(CanvasRenderPass.AfterNodes);
        values[3].Should().Be(CanvasRenderPass.TopMost);
    }

    [Fact]
    public void Context_pass_field_is_set_to_the_current_pass_during_render()
    {
        CanvasRenderPass? capturedPass = null;
        var r = new LambdaRenderer("cap", CanvasRenderPass.TopMost, ctx => capturedPass = ctx.Pass);
        var ctx = new FakeRenderContext();
        var all = new List<ICustomCanvasRenderer> { r };

        InvokePass(all, ctx, CanvasRenderPass.TopMost);

        capturedPass.Should().Be(CanvasRenderPass.TopMost);
    }

    [Fact]
    public void Empty_renderer_list_does_not_throw()
    {
        var ctx = new FakeRenderContext();
        var empty = new List<ICustomCanvasRenderer>();
        System.Action act = () =>
        {
            foreach (CanvasRenderPass pass in Enum.GetValues<CanvasRenderPass>())
                InvokePass(empty, ctx, pass);
        };
        act.Should().NotThrow();
    }

    // Helper renderer that records invocation order by name.
    private sealed class RecordingRendererWithOrder : ICustomCanvasRenderer
    {
        private readonly List<string> _order;
        public string Id { get; }
        public CanvasRenderPass Pass { get; }
        public bool IsActive => true;

        public RecordingRendererWithOrder(string id, CanvasRenderPass pass, List<string> order)
        {
            Id     = id;
            Pass   = pass;
            _order = order;
        }

        public void Render(ICanvasRenderContext ctx) => _order.Add(Id);
        public void Dispose() { }
    }

    // Helper renderer backed by a lambda.
    private sealed class LambdaRenderer : ICustomCanvasRenderer
    {
        private readonly System.Action<ICanvasRenderContext> _render;
        public string Id { get; }
        public CanvasRenderPass Pass { get; }
        public bool IsActive => true;

        public LambdaRenderer(string id, CanvasRenderPass pass, System.Action<ICanvasRenderContext> render)
        {
            Id      = id;
            Pass    = pass;
            _render = render;
        }

        public void Render(ICanvasRenderContext ctx) => _render(ctx);
        public void Dispose() { }
    }
}
