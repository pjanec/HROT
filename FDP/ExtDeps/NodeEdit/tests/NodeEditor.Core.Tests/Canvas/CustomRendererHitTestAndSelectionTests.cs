using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Canvas;

// ── Stubs ─────────────────────────────────────────────────────────────────────

// Fake IHitTestContext with settable data.
file sealed class FakeHitTestContext : IHitTestContext
{
    public ViewportState Viewport { get; } = new ViewportState();
    public IGraphModel Graph => throw new NotImplementedException();
    public IReadOnlySet<NodeId> VisibleNodes { get; } = new HashSet<NodeId>();
    public IReadOnlySet<LinkId> VisibleLinks { get; }  = new HashSet<LinkId>();
    public float Zoom => 1f;
}

// Renderer that always returns a hit.
file sealed class AlwaysHitRenderer : ICustomCanvasRenderer, ICustomCanvasHitTester
{
    public string Id { get; }
    public CanvasRenderPass Pass { get; }
    public bool IsActive { get; set; } = true;
    public CustomElementKind HitKind { get; set; } = CustomElementKind.Standalone;
    public int HitCount { get; private set; }

    public AlwaysHitRenderer(string id, CanvasRenderPass pass) { Id = id; Pass = pass; }

    public void Render(ICanvasRenderContext ctx) { }
    public void Dispose() { }

    public CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx)
    {
        HitCount++;
        return new CustomElementHit("elem1", HitKind, default);
    }
}

// Renderer that never returns a hit.
file sealed class NeverHitRenderer : ICustomCanvasRenderer, ICustomCanvasHitTester
{
    public string Id { get; }
    public CanvasRenderPass Pass { get; }
    public bool IsActive => true;

    public NeverHitRenderer(string id, CanvasRenderPass pass) { Id = id; Pass = pass; }

    public void Render(ICanvasRenderContext ctx) { }
    public void Dispose() { }

    public CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx) => null;
}

// Renderer that records OnElementSelected / OnElementDeselected callbacks.
file sealed class SelectableRenderer : ICustomCanvasRenderer, ICustomCanvasHitTester, ICustomCanvasSelectable
{
    public string Id { get; }
    public CanvasRenderPass Pass { get; }
    public bool IsActive => true;

    public List<string> SelectedKeys  { get; } = new();
    public List<string> DeselectedKeys { get; } = new();

    public SelectableRenderer(string id, CanvasRenderPass pass) { Id = id; Pass = pass; }

    public void Render(ICanvasRenderContext ctx) { }
    public void Dispose() { }

    public CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx) =>
        new CustomElementHit("key-A", CustomElementKind.NodeAdornment, default);

    public void OnElementSelected(string elementKey, CustomElementHit hit) =>
        SelectedKeys.Add(elementKey);

    public void OnElementDeselected(string elementKey) =>
        DeselectedKeys.Add(elementKey);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public sealed class CustomRendererHitTestTests
{
    // Helper: simulate the SubmitCustomHits logic used inside HitTester.
    private static CustomElementRef? RunHitTest(
        IReadOnlyList<ICustomCanvasRenderer> renderers,
        CanvasRenderPass pass,
        Vector2 point,
        IHitTestContext ctx)
    {
        int count = renderers.Count;
        // Later-registered wins, so iterate in reverse and return first hit.
        for (int i = count - 1; i >= 0; i--)
        {
            var r = renderers[i];
            if (r.Pass != pass || !r.IsActive) continue;
            if (r is not ICustomCanvasHitTester ht) continue;
            var result = ht.HitTest(point, ctx);
            if (result is not null)
                return new CustomElementRef(r.Id, result.Value.ElementKey);
        }
        return null;
    }

    [Fact]
    public void Renderer_not_implementing_ICustomCanvasHitTester_is_skipped()
    {
        // A renderer that does not implement the hit-test interface should not be tested.
        var pure  = new CustomRendererPassTests_FakeRenderer("pure", CanvasRenderPass.AfterWires);
        var ctx   = new FakeHitTestContext();
        var all   = new List<ICustomCanvasRenderer> { pure };

        var hit = RunHitTest(all, CanvasRenderPass.AfterWires, Vector2.Zero, ctx);

        hit.Should().BeNull();
    }

    [Fact]
    public void Renderer_returning_non_null_is_reported_as_hit()
    {
        var r   = new AlwaysHitRenderer("r1", CanvasRenderPass.AfterNodes);
        var ctx = new FakeHitTestContext();
        var all = new List<ICustomCanvasRenderer> { r };

        var hit = RunHitTest(all, CanvasRenderPass.AfterNodes, Vector2.Zero, ctx);

        hit.Should().NotBeNull();
        hit!.Value.RendererId.Should().Be("r1");
        hit!.Value.ElementKey.Should().Be("elem1");
    }

    [Fact]
    public void Inactive_renderer_is_skipped_during_hit_test()
    {
        var r   = new AlwaysHitRenderer("r1", CanvasRenderPass.TopMost) { IsActive = false };
        var ctx = new FakeHitTestContext();
        var all = new List<ICustomCanvasRenderer> { r };

        var hit = RunHitTest(all, CanvasRenderPass.TopMost, Vector2.Zero, ctx);

        hit.Should().BeNull();
    }

    [Fact]
    public void Last_registered_renderer_wins_over_first()
    {
        var first  = new AlwaysHitRenderer("first",  CanvasRenderPass.AfterWires);
        var second = new AlwaysHitRenderer("second", CanvasRenderPass.AfterWires);
        var ctx    = new FakeHitTestContext();
        var all    = new List<ICustomCanvasRenderer> { first, second };

        var hit = RunHitTest(all, CanvasRenderPass.AfterWires, Vector2.Zero, ctx);

        // later-registered (second) wins
        hit!.Value.RendererId.Should().Be("second");
    }

    [Fact]
    public void Miss_pass_returns_null_even_when_renderer_would_hit()
    {
        var r   = new AlwaysHitRenderer("r1", CanvasRenderPass.AfterNodes);
        var ctx = new FakeHitTestContext();
        var all = new List<ICustomCanvasRenderer> { r };

        // Hit-test against a different pass
        var hit = RunHitTest(all, CanvasRenderPass.BeforeContent, Vector2.Zero, ctx);

        hit.Should().BeNull();
    }
}

public sealed class CustomRendererSelectionTests
{
    [Fact]
    public void SelectionEntry_OfCustomElement_round_trips()
    {
        var ceRef = new CustomElementRef("renderer-1", "elem-A");
        var entry = SelectionEntry.OfCustomElement(ceRef);

        entry.Kind.Should().Be(SelectionEntryKind.CustomElement);
        entry.CustomElement.Should().Be(ceRef);
    }

    [Fact]
    public void SelectionState_CustomElements_enumerates_only_custom_entries()
    {
        var state = new SelectionState();
        state.Add(SelectionEntry.OfCustomElement(new CustomElementRef("r1", "e1")));
        state.Add(SelectionEntry.OfCustomElement(new CustomElementRef("r2", "e2")));
        state.Add(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        var customs = state.CustomElements.ToList();
        customs.Should().HaveCount(2);
        customs.Should().Contain(new CustomElementRef("r1", "e1"));
        customs.Should().Contain(new CustomElementRef("r2", "e2"));
    }

    [Fact]
    public void ICustomCanvasSelectable_interface_is_optional_companion()
    {
        // A renderer can implement ICustomCanvasHitTester without ICustomCanvasSelectable.
        ICustomCanvasRenderer r = new AlwaysHitRenderer("r1", CanvasRenderPass.AfterNodes);
        (r is ICustomCanvasSelectable).Should().BeFalse();
    }

    [Fact]
    public void SelectableRenderer_implements_both_companion_interfaces()
    {
        ICustomCanvasRenderer r = new SelectableRenderer("r1", CanvasRenderPass.AfterNodes);
        (r is ICustomCanvasHitTester).Should().BeTrue();
        (r is ICustomCanvasSelectable).Should().BeTrue();
    }
}

// Minimal renderer stub for cross-test use.
file sealed class CustomRendererPassTests_FakeRenderer : ICustomCanvasRenderer
{
    public string Id { get; }
    public CanvasRenderPass Pass { get; }
    public bool IsActive => true;

    public CustomRendererPassTests_FakeRenderer(string id, CanvasRenderPass pass)
    {
        Id   = id;
        Pass = pass;
    }

    public void Render(ICanvasRenderContext ctx) { }
    public void Dispose() { }
}
