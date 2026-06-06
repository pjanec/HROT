using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for FIX-A (BF-BATCH-0607): unconnected In-data pins whose type has a registered
/// editor expose a non-null <see cref="IPinModel.Default"/> (type-zero) even when
/// <see cref="Pin.DefaultValue"/> is not yet set.
/// </summary>
public sealed class BlueprintPinDefaultZeroTests
{
    // ── A stub registry that reports editors for known numeric types ──────────

    private sealed class StubEditorRegistry : IPinDefaultValueEditorRegistry
    {
        private static readonly HashSet<string> _supported = new()
        {
            "System.Boolean", "System.Int32", "System.Single",
            "System.Double", "System.Byte", "System.UInt32", "System.String",
        };

        public void Register(TypeKey type, IPinDefaultValueEditor editor) { }
        public void RegisterFallback(IPinDefaultValueEditor editor) { }

        public IPinDefaultValueEditor? GetEditor(TypeKey type)
            => _supported.Contains(type.Id) ? NullEditor.Instance : null;

        // A no-op editor stub — only used to confirm "has editor" in registry checks.
        private sealed class NullEditor : IPinDefaultValueEditor
        {
            public static readonly NullEditor Instance = new();
            public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
            {
                committed = false;
                return false;
            }
        }
    }

    // ── FIX-A: unset pin + registry with editor → non-null Default (type-zero) ─

    [Theory]
    [InlineData("System.Int32",   0)]
    [InlineData("System.Single",  0f)]
    [InlineData("System.Boolean", false)]
    [InlineData("System.String",  "")]
    public void PinModel_WithRegistry_UnsetPin_HasDefault_TypeZero(string typeId, object expectedZero)
    {
        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "value",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = typeId },
            // DefaultValue NOT set
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId, new StubEditorRegistry());

        Assert.NotNull(model.Default);
        Assert.Equal(expectedZero, model.Default!.Value);
    }

    // ── Legacy: unset pin WITHOUT registry → still null ───────────────────────

    [Fact]
    public void PinModel_NoRegistry_UnsetPin_DefaultIsNull()
    {
        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "count",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Int32" },
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId);   // two-arg ctor

        Assert.Null(model.Default);
    }

    // ── Unsupported type + registry → null (no blank widget) ─────────────────

    [Fact]
    public void PinModel_WithRegistry_UnsupportedType_DefaultIsNull()
    {
        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "entity",
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" },
        };
        var nodeId = new NodeId(Guid.NewGuid());
        // StubEditorRegistry does NOT register an editor for Fdp.Core.Entity.
        var model  = new BlueprintPinModel(pin, nodeId, new StubEditorRegistry());

        Assert.Null(model.Default);
    }

    // ── Connected / Out / Exec pins → null regardless of registry ────────────

    [Fact]
    public void PinModel_WithRegistry_OutputPin_DefaultIsNull()
    {
        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "result",
            Direction = "Out",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Int32" },
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId, new StubEditorRegistry());

        Assert.Null(model.Default);
    }

    [Fact]
    public void PinModel_WithRegistry_ExecPin_DefaultIsNull()
    {
        var pin = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "exec",
            Direction = "In",
            IsExec    = true,
            TypeRef   = new BlueprintTypeRef { TypeId = "" },
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId, new StubEditorRegistry());

        Assert.Null(model.Default);
    }

    // ── Persisted value always shown regardless of registry ───────────────────

    [Fact]
    public void PinModel_WithRegistry_PersistedDefault_ShowsPersistedValue()
    {
        var pin = new Pin
        {
            Id           = Guid.NewGuid(),
            Name         = "speed",
            Direction    = "In",
            IsExec       = false,
            TypeRef      = new BlueprintTypeRef { TypeId = "System.Single" },
            DefaultValue = "3.14",
        };
        var nodeId = new NodeId(Guid.NewGuid());
        var model  = new BlueprintPinModel(pin, nodeId, new StubEditorRegistry());

        Assert.NotNull(model.Default);
        Assert.Equal(3.14f, model.Default!.Value);
    }

    // ── BlueprintPinDefaultValue.ParseValue: null/empty → type-zero ──────────

    [Theory]
    [InlineData("System.Int32",   null, 0)]
    [InlineData("System.Int32",   "",   0)]
    [InlineData("System.Single",  null, 0f)]
    [InlineData("System.Boolean", null, false)]
    [InlineData("System.String",  null, "")]
    public void ParseValue_NullOrEmpty_ReturnsTypeZero(string typeId, string? rawValue, object expectedZero)
    {
        var result = BlueprintPinDefaultValue.ParseValue(typeId, rawValue);
        Assert.Equal(expectedZero, result);
    }

    [Fact]
    public void ParseValue_NullOrEmpty_UnknownType_ReturnsNull()
    {
        var result = BlueprintPinDefaultValue.ParseValue("Unknown.Type", null);
        Assert.Null(result);
    }
}
