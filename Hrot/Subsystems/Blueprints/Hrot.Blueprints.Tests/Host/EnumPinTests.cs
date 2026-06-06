using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using NodeEditor.UI.MiniEditors;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Headless tests for AN6 — Blueprint enum data pin support.
/// Coverage:
///   (a) <see cref="BlueprintEnumValueProvider"/> returns members for a <c>global::</c> TypeKey
///       pointing at a real project enum that is loaded in the test process.
///   (b) <see cref="BlueprintEnumValueProvider"/> returns empty for a non-<c>global::</c> key.
///   (c) <see cref="EnumSentinelPinEditorRegistry"/> returns an <see cref="EnumPinEditor"/> for
///       <c>global::</c>-prefixed TypeKeys and delegates to the inner registry for primitives /
///       FixedString.
///   (d) <see cref="BlueprintPinDefaultValue.ParseValue"/> round-trips enum values as
///       <c>long</c> for <c>global::</c>-prefixed TypeIds; null/empty → 0L.
///   (e) <see cref="BlueprintPinModel"/> exposes a non-null Default for a <c>global::</c>-typed
///       In-data pin when the wrapper registry is supplied.
///   (f) <see cref="NodePinSchema.ChannelCommandPins"/> / <see cref="NodePinSchema.ReflectDataMembers"/>
///       stamps an enum DTO field's TypeId with the <c>global::</c> prefix.
/// NOTE: ImGui combo rendering is a REVIEW-V1 item; this file is fully headless.
/// </summary>
public sealed class EnumPinTests
{
    // Real project enum loaded in this test process.
    // GraphKind lives in Hrot.Blueprints.Core.Assets (referenced by the test project).
    private const string GraphKindFqn    = "Hrot.Blueprints.Core.Assets.GraphKind";
    private const string GraphKindTypeId = "global::" + GraphKindFqn;

    // ── (a) BlueprintEnumValueProvider: returns members for a real enum ───────

    [Fact]
    public void EnumValueProvider_ReturnsMembers_ForGlobalPrefixedTypeKey()
    {
        var provider = new BlueprintEnumValueProvider();
        var typeKey  = new TypeKey(GraphKindTypeId);

        var entries = provider.GetValues(typeKey);

        Assert.NotEmpty(entries);
        // GraphKind has 3 members: Function=0, Event=1, Construction=2
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.DisplayName == "Function" && e.Value == 0L);
        Assert.Contains(entries, e => e.DisplayName == "Event"    && e.Value == 1L);
        Assert.Contains(entries, e => e.DisplayName == "Construction" && e.Value == 2L);
    }

    [Fact]
    public void EnumValueProvider_ReturnsEmpty_ForNonGlobalKey()
    {
        var provider = new BlueprintEnumValueProvider();
        var typeKey  = new TypeKey("System.Int32"); // not a global:: key

        var entries = provider.GetValues(typeKey);

        Assert.Empty(entries);
    }

    [Fact]
    public void EnumValueProvider_ReturnsEmpty_ForUnresolvableFqn()
    {
        var provider = new BlueprintEnumValueProvider();
        var typeKey  = new TypeKey("global::Does.Not.Exist.SomeEnum");

        var entries = provider.GetValues(typeKey);

        Assert.Empty(entries);
    }

    [Fact]
    public void EnumValueProvider_GetMaxInlineValues_IsAtLeastEight()
    {
        var provider = new BlueprintEnumValueProvider();
        Assert.True(provider.GetMaxInlineValues() >= 8);
    }

    // ── (b) second call hits the cache (no regression: still returns members) ──

    [Fact]
    public void EnumValueProvider_CachedCall_SameResult()
    {
        var provider = new BlueprintEnumValueProvider();
        var typeKey  = new TypeKey(GraphKindTypeId);

        var first  = provider.GetValues(typeKey);
        var second = provider.GetValues(typeKey); // should hit cache

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].DisplayName, second[i].DisplayName);
            Assert.Equal(first[i].Value,       second[i].Value);
        }
    }

    // ── (c) EnumSentinelPinEditorRegistry ─────────────────────────────────────

    [Fact]
    public void SentinelRegistry_ReturnsEnumPinEditor_ForGlobalKey()
    {
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        var wrapped  = new EnumSentinelPinEditorRegistry(inner, provider);

        var editor = wrapped.GetEditor(new TypeKey(GraphKindTypeId));

        Assert.NotNull(editor);
        Assert.IsType<EnumPinEditor>(editor);
    }

    [Fact]
    public void SentinelRegistry_DelegatesToInner_ForIntKey()
    {
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        var wrapped  = new EnumSentinelPinEditorRegistry(inner, provider);

        var editor = wrapped.GetEditor(new TypeKey("System.Int32"));

        Assert.NotNull(editor);
        Assert.IsType<IntPinEditor>(editor);
    }

    [Fact]
    public void SentinelRegistry_DelegatesToInner_ForBoolKey()
    {
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        var wrapped  = new EnumSentinelPinEditorRegistry(inner, provider);

        var editor = wrapped.GetEditor(new TypeKey("System.Boolean"));

        Assert.NotNull(editor);
        Assert.IsType<BoolPinEditor>(editor);
    }

    [Fact]
    public void SentinelRegistry_ReturnsNull_ForUnknownNonGlobalKey()
    {
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        var wrapped  = new EnumSentinelPinEditorRegistry(inner, provider);

        var editor = wrapped.GetEditor(new TypeKey("Unknown.Random.Type"));

        Assert.Null(editor);
    }

    [Fact]
    public void SentinelRegistry_Register_ForwardsToInner()
    {
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        var wrapped  = new EnumSentinelPinEditorRegistry(inner, provider);

        // Register FixedString32 as a string editor (mirrors production wiring).
        wrapped.Register(new TypeKey(BlueprintTypeSystem.FixedString32), new StringPinEditor());
        var editor = wrapped.GetEditor(new TypeKey(BlueprintTypeSystem.FixedString32));

        Assert.NotNull(editor);
        Assert.IsType<StringPinEditor>(editor);
    }

    // ── (d) BlueprintPinDefaultValue.ParseValue enum round-trip ──────────────

    [Theory]
    [InlineData("global::SomeNs.SomeEnum", "0",  0L)]
    [InlineData("global::SomeNs.SomeEnum", "2",  2L)]
    [InlineData("global::SomeNs.SomeEnum", "42", 42L)]
    public void ParseValue_EnumGlobalPrefix_ReturnsLong(string typeId, string raw, long expected)
    {
        var result = BlueprintPinDefaultValue.ParseValue(typeId, raw);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseValue_EnumGlobalPrefix_NullOrEmpty_ReturnsZeroLong(string? raw)
    {
        var result = BlueprintPinDefaultValue.ParseValue("global::SomeNs.SomeEnum", raw);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ParseValue_EnumGlobalPrefix_BadString_ReturnsZeroLong()
    {
        var result = BlueprintPinDefaultValue.ParseValue("global::SomeNs.SomeEnum", "notanumber");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ParseValue_NonGlobalPrefix_Unchanged()
    {
        // Non-global prefix still behaves as the raw-string fallback.
        var result = BlueprintPinDefaultValue.ParseValue("Some.Unknown.Type", "rawval");
        Assert.Equal("rawval", result);
    }

    [Fact]
    public void FormatValue_Long_ReturnsDecimalString()
    {
        // EnumPinEditor.Draw sets value = (long)entries[selectedIdx].Value.
        // FormatValue must round-trip it back to a decimal string for PinDefaults.
        var result = BlueprintPinDefaultValue.FormatValue(2L);
        Assert.Equal("2", result);
    }

    // ── (e) BlueprintPinModel.Default for enum pin ────────────────────────────

    [Fact]
    public void PinModel_Default_IsNonNull_ForEnumPin_WithRegistry()
    {
        // Simulate wiring done in BlueprintDocumentFactory.Build.
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        IPinDefaultValueEditorRegistry registry = new EnumSentinelPinEditorRegistry(inner, provider);

        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "mode",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = GraphKindTypeId },
            // No DefaultValue persisted yet — registry triggers type-zero path.
        };
        var model = new BlueprintPinModel(pin, new NodeId(Guid.NewGuid()), registry);

        Assert.NotNull(model.Default);
        // Type-zero for enum is 0L.
        Assert.Equal(0L, model.Default!.Value);
    }

    [Fact]
    public void PinModel_Default_ParsesLong_ForPersistedEnumDefault()
    {
        var inner    = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        var provider = new BlueprintEnumValueProvider();
        IPinDefaultValueEditorRegistry registry = new EnumSentinelPinEditorRegistry(inner, provider);

        var pin = new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "mode",
            Direction    = "In",
            IsExec       = false,
            TypeRef      = new BlueprintTypeRef { TypeId = GraphKindTypeId },
            DefaultValue = "1",
        };
        var model = new BlueprintPinModel(pin, new NodeId(Guid.NewGuid()), registry);

        Assert.NotNull(model.Default);
        Assert.Equal(1L, model.Default!.Value);
    }

    // ── (f) NodePinSchema.ReflectDataMembers stamps enum field TypeIds ─────────

    /// <summary>
    /// A minimal DTO with one enum field, simulating a ChannelCommand params struct.
    /// </summary>
    private sealed class DtoWithEnumField
    {
        public GraphKind Mode;
        public int       Count;
    }

    [Fact]
    public void NodePinSchema_ChannelCommandPins_StampsEnumFieldWithGlobalPrefix()
    {
        // Build a fake channel-command catalog entry whose ParamsTypeFqn points to our DTO.
        var dtoFqn = typeof(DtoWithEnumField).FullName!;
        var cc = new Hrot.Blueprints.Core.Assets.ChannelCommandNode
        {
            Id          = Guid.NewGuid(),
            ChannelType = "Test",
            ActionId    = "TestAction",
        };

        // ChannelCommandCatalogEntry(Name, ChannelTypeFqn, ActionId:ushort, ParamsTypeFqn)
        var catalog = new FakeChannelCommandCatalog(new[]
        {
            new Hrot.Blueprints.Core.Compiler.Catalogs.ChannelCommandCatalogEntry(
                Name:           "TestAction",
                ChannelTypeFqn: "TestNamespace.Test",
                ActionId:       1,
                ParamsTypeFqn:  dtoFqn)
        });

        var pins = NodePinSchema.GetCanonicalPins(cc, channelCommands: catalog);

        // Find the "Mode" pin (the enum field).
        var modePin = pins.FirstOrDefault(p => p.Name == "Mode" && !p.IsExec);
        Assert.NotNull(modePin);
        // TypeId must start with "global::" (the AN2 enum sentinel).
        Assert.StartsWith("global::", modePin!.TypeRef.TypeId, StringComparison.Ordinal);
        Assert.Contains(nameof(GraphKind), modePin.TypeRef.TypeId, StringComparison.Ordinal);

        // The plain int field must NOT be prefixed with "global::".
        var countPin = pins.FirstOrDefault(p => p.Name == "Count" && !p.IsExec);
        Assert.NotNull(countPin);
        Assert.DoesNotContain("global::", countPin!.TypeRef.TypeId);
    }

    // ── fake helpers ──────────────────────────────────────────────────────────

    private sealed class FakeChannelCommandCatalog
        : Hrot.Blueprints.Core.Compiler.Catalogs.IChannelCommandCatalog
    {
        private readonly IReadOnlyList<Hrot.Blueprints.Core.Compiler.Catalogs.ChannelCommandCatalogEntry> _entries;

        public FakeChannelCommandCatalog(
            IReadOnlyList<Hrot.Blueprints.Core.Compiler.Catalogs.ChannelCommandCatalogEntry> entries)
            => _entries = entries;

        public IReadOnlyList<Hrot.Blueprints.Core.Compiler.Catalogs.ChannelCommandCatalogEntry>
            GetEntries() => _entries;
    }
}
