using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Slice 2a-3 — headless tests asserting <see cref="GetSharedNode"/>/<see cref="SetSharedNode"/>
/// palette descriptors exist under <see cref="BlueprintNodePaletteEntries.Categories.SharedState"/>,
/// and that they're reachable through the full bootstrap palette registry/catalog, mirroring
/// the pattern established by <c>BlueprintMathPaletteEntriesTests</c> / the palette assertions in
/// <c>BcpBatch02BlueprintTests.Palette_RegistersFullBlueprintNodeSet_WithCategories</c>.
/// All tests are headless (no ImGui).
/// </summary>
public sealed class SharedNodePaletteEntriesTests
{
    private static IReadOnlyList<NodeKindDescriptor> AllDescriptors()
        => BlueprintNodePaletteEntries.All().ToList();

    // ── Descriptor shape ──────────────────────────────────────────────────────

    [Fact]
    public void All_IncludesGetShared_UnderSharedStateCategory()
    {
        var descriptor = AllDescriptors().Single(d => d.Kind == "GetShared");

        Assert.Equal("Get Shared", descriptor.DisplayName);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.SharedState, descriptor.Category);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Tooltip));
    }

    [Fact]
    public void All_IncludesSetShared_UnderSharedStateCategory()
    {
        var descriptor = AllDescriptors().Single(d => d.Kind == "SetShared");

        Assert.Equal("Set Shared", descriptor.DisplayName);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.SharedState, descriptor.Category);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Tooltip));
    }

    [Fact]
    public void Categories_SharedState_IsDistinctFromVariables()
    {
        Assert.NotEqual(
            BlueprintNodePaletteEntries.Categories.Variables,
            BlueprintNodePaletteEntries.Categories.SharedState);
    }

    // ── CreateInstance ────────────────────────────────────────────────────────

    [Fact]
    public void CreateInstance_GetShared_ReturnsGetSharedNode_WithEmptyFieldsAndFreshId()
    {
        var descriptor = AllDescriptors().Single(d => d.Kind == "GetShared");

        var node1 = descriptor.CreateInstance();
        var node2 = descriptor.CreateInstance();

        var gsn1 = Assert.IsType<GetSharedNode>(node1);
        var gsn2 = Assert.IsType<GetSharedNode>(node2);

        Assert.Equal("", gsn1.VariableId);
        Assert.Equal("", gsn1.SharedTypeId);
        Assert.NotEqual(Guid.Empty, gsn1.Id);
        Assert.NotEqual(gsn1.Id, gsn2.Id);
    }

    [Fact]
    public void CreateInstance_SetShared_ReturnsSetSharedNode_WithEmptyFieldsAndFreshId()
    {
        var descriptor = AllDescriptors().Single(d => d.Kind == "SetShared");

        var node1 = descriptor.CreateInstance();
        var node2 = descriptor.CreateInstance();

        var ssn1 = Assert.IsType<SetSharedNode>(node1);
        var ssn2 = Assert.IsType<SetSharedNode>(node2);

        Assert.Equal("", ssn1.VariableId);
        Assert.Equal("", ssn1.SharedTypeId);
        Assert.NotEqual(Guid.Empty, ssn1.Id);
        Assert.NotEqual(ssn1.Id, ssn2.Id);
    }

    // ── Full bootstrap registry / catalog reachability ───────────────────────

    [Fact]
    public void PaletteRegistry_ContainsGetSharedAndSetShared()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        var getShared = registry.TryGet("GetShared");
        var setShared = registry.TryGet("SetShared");

        Assert.NotNull(getShared);
        Assert.NotNull(setShared);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.SharedState, getShared!.Category);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.SharedState, setShared!.Category);
    }

    [Fact]
    public void NodeCatalog_All_ContainsGetSharedAndSetShared()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();
        var catalog  = new BlueprintNodeCatalog(registry);

        Assert.Contains(catalog.All, e => e.Kind.Id == "GetShared");
        Assert.Contains(catalog.All, e => e.Kind.Id == "SetShared");
    }

    [Fact]
    public void Palette_CreateInstance_ViaRegistry_ReturnsTypedNodes()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        Assert.IsType<GetSharedNode>(registry.TryGet("GetShared")!.CreateInstance());
        Assert.IsType<SetSharedNode>(registry.TryGet("SetShared")!.CreateInstance());
    }

    // ── Pin projection parity (2a-2 twins; verifying, not reimplementing) ─────

    [Fact]
    public void PinProjection_GetShared_PureValueAndFoundOut_NoExec()
    {
        var node = new GetSharedNode { Id = Guid.NewGuid(), SharedTypeId = "global::My.Struct" };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.False(pins.Any(p => p.IsExec), "GetShared is pure — must have no exec pins.");
        Assert.Contains(pins, p => p.Name == "Value" && p.Direction == "Out");
        Assert.Contains(pins, p => p.Name == "Found" && p.Direction == "Out" && p.TypeRef!.TypeId == "System.Boolean");
    }

    [Fact]
    public void PinProjection_SetShared_ExecInOut_PlusTypedValueInAndWrittenOut()
    {
        var node = new SetSharedNode { Id = Guid.NewGuid(), SharedTypeId = "global::My.Struct" };

        var pins = NodePinSchema.GetCanonicalPins(node);

        Assert.Contains(pins, p => p.IsExec && p.Direction == "In");
        Assert.Contains(pins, p => p.IsExec && p.Direction == "Out");
        Assert.Contains(pins, p => !p.IsExec && p.Name == "Value"   && p.Direction == "In");
        Assert.Contains(pins, p => !p.IsExec && p.Name == "Written" && p.Direction == "Out"
                                              && p.TypeRef!.TypeId == "System.Boolean");
    }
}
