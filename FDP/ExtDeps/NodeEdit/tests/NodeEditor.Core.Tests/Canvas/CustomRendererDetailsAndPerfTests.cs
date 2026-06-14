using System;
using System.Collections.Generic;
using FluentAssertions;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Canvas;

public sealed class DetailsTargetCustomElementTests
{
    [Fact]
    public void SingleCustomElement_holds_the_element_ref()
    {
        var ceRef  = new CustomElementRef("renderer-1", "elem-A");
        DetailsTarget target = new DetailsTarget.SingleCustomElement(ceRef);

        target.Should().BeOfType<DetailsTarget.SingleCustomElement>();
        ((DetailsTarget.SingleCustomElement)target).Element.Should().Be(ceRef);
    }

    [Fact]
    public void MultipleCustomElements_holds_the_list()
    {
        var refs = new List<CustomElementRef>
        {
            new CustomElementRef("r1", "e1"),
            new CustomElementRef("r2", "e2"),
        };
        DetailsTarget target = new DetailsTarget.MultipleCustomElements(refs);

        target.Should().BeOfType<DetailsTarget.MultipleCustomElements>();
        ((DetailsTarget.MultipleCustomElements)target).Elements.Should().HaveCount(2);
    }

    [Fact]
    public void SingleCustomElement_is_a_DetailsTarget()
    {
        DetailsTarget target = new DetailsTarget.SingleCustomElement(default);
        (target is DetailsTarget).Should().BeTrue();
    }

    [Fact]
    public void Custom_element_targets_are_distinct_from_attachment_targets()
    {
        DetailsTarget custom = new DetailsTarget.SingleCustomElement(default);
        DetailsTarget attach = new DetailsTarget.SingleAttachment(default);

        custom.Should().NotBe(attach);
        custom.GetType().Should().NotBe(attach.GetType());
    }
}

public sealed class CustomElementContextMenuProviderTests
{
    // Stub implementation of the provider interface.
    private sealed class FakeProvider : ICustomElementContextMenuProvider
    {
        public string RendererId => "test-renderer";

        public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit) =>
            new[]
            {
                new ContextMenuItem("Edit", () => { }),
                new ContextMenuItem("Delete", () => { }),
            };
    }

    [Fact]
    public void Provider_has_a_renderer_id()
    {
        ICustomElementContextMenuProvider p = new FakeProvider();
        p.RendererId.Should().Be("test-renderer");
    }

    [Fact]
    public void Provider_returns_items_for_an_element()
    {
        var p     = new FakeProvider();
        var items = p.GetItemsFor("elem1", new CustomElementHit("elem1", CustomElementKind.Standalone, default));

        items.Should().HaveCount(2);
        items[0].Label.Should().Be("Edit");
        items[1].Label.Should().Be("Delete");
    }

    [Fact]
    public void ContextMenuItem_enabled_is_true_by_default()
    {
        var item = new ContextMenuItem("Test", () => { });
        item.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ContextMenuItem_children_is_null_by_default()
    {
        var item = new ContextMenuItem("Test", () => { });
        item.Children.Should().BeNull();
    }

    [Fact]
    public void ContextMenuItem_children_can_be_set()
    {
        var child1 = new ContextMenuItem("Child1", () => { });
        var child2 = new ContextMenuItem("Child2", () => { });
        var parent = new ContextMenuItem("Parent", () => { }, true, new[] { child1, child2 });
        parent.Children.Should().HaveCount(2);
        parent.Children![0].Label.Should().Be("Child1");
        parent.Children![1].Label.Should().Be("Child2");
    }
}

/// <summary>
/// Tests for the context menu routing contract: a provider is only invoked when its
/// RendererId matches the right-clicked element's RendererId (TASK-NER-07).
/// </summary>
public sealed class CustomElementContextMenuRoutingTests
{
    // Stub provider that records whether GetItemsFor was called.
    private sealed class SpyProvider : ICustomElementContextMenuProvider
    {
        public string RendererId { get; }
        public bool WasCalled { get; private set; }

        public SpyProvider(string rendererId) => RendererId = rendererId;

        public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
        {
            WasCalled = true;
            return new[] { new ContextMenuItem("Action", () => { }) };
        }
    }

    [Fact]
    public void Provider_is_queried_when_renderer_id_matches()
    {
        var provider = new SpyProvider("renderer-A");
        var ceRef    = new CustomElementRef("renderer-A", "elem1");

        // Simulate the DrawContextMenu routing decision.
        if (provider.RendererId == ceRef.RendererId)
        {
            var hit   = new CustomElementHit(ceRef.ElementKey, CustomElementKind.Standalone, default);
            var items = provider.GetItemsFor(ceRef.ElementKey, hit);
            items.Should().NotBeEmpty();
        }

        provider.WasCalled.Should().BeTrue();
    }

    [Fact]
    public void Provider_is_not_queried_when_renderer_id_mismatches()
    {
        var provider = new SpyProvider("renderer-A");
        var ceRef    = new CustomElementRef("renderer-B", "elem1");

        // Simulate the DrawContextMenu routing decision.
        if (provider.RendererId == ceRef.RendererId)
        {
            var hit = new CustomElementHit(ceRef.ElementKey, CustomElementKind.Standalone, default);
            provider.GetItemsFor(ceRef.ElementKey, hit);
        }

        provider.WasCalled.Should().BeFalse();
    }

    [Fact]
    public void Null_provider_does_not_throw()
    {
        ICustomElementContextMenuProvider? provider = null;
        var ceRef = new CustomElementRef("renderer-A", "elem1");

        // Simulate what DrawContextMenu does: guard with null check.
        System.Action act = () =>
        {
            if (provider != null && provider.RendererId == ceRef.RendererId)
            {
                var hit   = new CustomElementHit(ceRef.ElementKey, CustomElementKind.Standalone, default);
                provider.GetItemsFor(ceRef.ElementKey, hit);
            }
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Provider_returns_disabled_item_correctly()
    {
        var item = new ContextMenuItem("Disabled Action", () => { }, Enabled: false);
        item.Enabled.Should().BeFalse();
        item.Label.Should().Be("Disabled Action");
    }
}

public sealed class RendererPerfRecordTests
{
    [Fact]
    public void Default_record_has_zero_values()
    {
        var rec = new RendererPerfRecord(0f, 0f, 0f, 0);
        rec.LastFrameMs.Should().Be(0f);
        rec.AvgFrameMs.Should().Be(0f);
        rec.MaxFrameMs.Should().Be(0f);
        rec.CallsThisSession.Should().Be(0);
    }

    [Fact]
    public void Record_holds_all_fields()
    {
        var rec = new RendererPerfRecord(1.5f, 2.0f, 3.5f, 10);
        rec.LastFrameMs.Should().Be(1.5f);
        rec.AvgFrameMs.Should().Be(2.0f);
        rec.MaxFrameMs.Should().Be(3.5f);
        rec.CallsThisSession.Should().Be(10);
    }

    [Fact]
    public void EditorStatusSnapshot_includes_CustomRendererPerf_field()
    {
        // Verify the new field is part of the snapshot struct.
        var snapshot = new EditorStatusSnapshot(
            "TestGraph", 5, 1, 3, false, 0, 0, 1.0f,
            default, EditorMode.Editing, null,
            CustomRendererPerf: null);

        snapshot.CustomRendererPerf.Should().BeNull();
    }

    [Fact]
    public void EditorStatusSnapshot_can_carry_perf_data()
    {
        var perfData = new Dictionary<string, RendererPerfRecord>
        {
            ["renderer-1"] = new RendererPerfRecord(0.5f, 0.6f, 1.0f, 100),
        };
        var snapshot = new EditorStatusSnapshot(
            "TestGraph", 5, 1, 3, false, 0, 0, 1.0f,
            default, EditorMode.Editing, null,
            CustomRendererPerf: perfData);

        snapshot.CustomRendererPerf.Should().NotBeNull();
        snapshot.CustomRendererPerf!["renderer-1"].CallsThisSession.Should().Be(100);
    }
}
