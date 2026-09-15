using System.Runtime.CompilerServices;
using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.UnionSupport;
using StructEdit.Reflection;

namespace StructEdit.Tests.Session;

// ── Test fixtures ─────────────────────────────────────────────────────────────

[InlineArray(4)]
file struct FloatSlots4 { private float _e0; }

// A count-prefixed bounded list: the provider windows Slots to Used elements.
file struct BoundedListComponent
{
    public int Used;
    public FloatSlots4 Slots;
}

file struct PlainInlineComponent
{
    public int Tag;
    public FloatSlots4 Slots;
}

/// <summary>
/// Claims the [InlineArray] buffer of <see cref="BoundedListComponent"/> and presents a
/// bounded element window sized by the sibling <c>Used</c> field (clamped to capacity 4).
/// </summary>
file sealed class BoundedWindowProvider : IBufferViewProvider
{
    public bool CanCreateView(BufferViewRequest request)
        => request.ComponentType == typeof(BoundedListComponent)
           && request.BufferPath.Value == "$.Slots";

    public BufferViewResult CreateView(BufferViewRequest request)
    {
        int used = request.ReadSibling<int>(EditPath.Parse("$.Used"));
        return request.ProjectBufferAsElements(typeof(float), used, "BoundedSlots", capacity: 4);
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Provider-hook parity for <see cref="EditNodeKind.InlineArray"/> buffers (previously only
/// <see cref="EditNodeKind.FixedBuffer"/> consulted <see cref="IBufferViewProvider"/>s) plus
/// the bounded-window projection helper
/// <see cref="BufferViewRequest.ProjectBufferAsElements"/>.
/// </summary>
public class InlineArrayViewProviderTests
{
    private static IComponentEditService BuildService()
        => new ComponentEditServiceBuilder()
            .RegisterBufferViewProvider(new BoundedWindowProvider())
            .Build();

    [Fact]
    public void Provider_ClaimsInlineArray_WindowSizedBySiblingCount()
    {
        var component = new BoundedListComponent { Used = 2 };
        var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
            ref Unsafe.As<FloatSlots4, float>(ref component.Slots), 4);
        span[0] = 1.5f; span[1] = 2.5f; span[2] = 99f;   // [2] is beyond the window

        var service = BuildService();
        using var session = service.Open(component, typeof(BoundedListComponent));

        var view = session.Document.Root.Children.FirstOrDefault(c => c.Kind == EditNodeKind.BufferView);
        view.Should().NotBeNull("the provider must replace the raw InlineArray node");
        view!.Name.Should().Be("BoundedSlots");
        view.Children.Should().HaveCount(2, "the window is Used=2, not the capacity 4");
        view.Children[0].Binding!.GetBoxed().Should().Be(1.5f);
        view.Children[1].Binding!.GetBoxed().Should().Be(2.5f);
    }

    [Fact]
    public void ProjectBufferAsElements_ClampsWindowToCapacity_AndFloorsNegative()
    {
        var over = new BoundedListComponent { Used = 99 };
        var service = BuildService();
        using (var session = service.Open(over, typeof(BoundedListComponent)))
        {
            var view = session.Document.Root.Children.First(c => c.Kind == EditNodeKind.BufferView);
            view.Children.Should().HaveCount(4, "a corrupt over-capacity count clamps to capacity");
        }

        var negative = new BoundedListComponent { Used = -3 };
        using (var session = service.Open(negative, typeof(BoundedListComponent)))
        {
            var view = session.Document.Root.Children.First(c => c.Kind == EditNodeKind.BufferView);
            view.Children.Should().BeEmpty("a negative count floors to an empty window");
        }
    }

    [Fact]
    public void Provider_ElementBindings_WriteThroughToNativeBytes()
    {
        var component = new BoundedListComponent { Used = 1 };
        var service = BuildService();
        using var session = service.Open(component, typeof(BoundedListComponent));

        var view = session.Document.Root.Children.First(c => c.Kind == EditNodeKind.BufferView);
        view.Children[0].Binding!.SetBoxed(7.25f);

        var result = (BoundedListComponent)session.Commit();
        var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
            ref Unsafe.As<FloatSlots4, float>(ref result.Slots), 4);
        span[0].Should().Be(7.25f);
    }

    [Fact]
    public void UnclaimedInlineArray_KeepsDefaultFixedCountExpansion()
    {
        // The provider does not claim PlainInlineComponent -> the InlineArray node keeps its
        // default all-capacity element expansion (regression guard for the new hook).
        var service = BuildService();
        using var session = service.Open(new PlainInlineComponent(), typeof(PlainInlineComponent));

        var slots = session.Document.Root.Children.FirstOrDefault(c => c.Name == "Slots");
        slots.Should().NotBeNull();
        slots!.Kind.Should().Be(EditNodeKind.InlineArray);
        slots.Children.Should().HaveCount(4, "unclaimed inline arrays expand to full capacity as before");
    }
}
