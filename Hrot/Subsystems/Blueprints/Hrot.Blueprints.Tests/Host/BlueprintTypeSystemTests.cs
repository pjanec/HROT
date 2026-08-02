using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for <see cref="BlueprintTypeSystem"/>.
/// All tests are headless: no ImGui context required.
/// </summary>
public sealed class BlueprintTypeSystemTests
{
    private static BlueprintTypeSystem MakeSut()
        => new(NullPinDefaultValueEditorRegistry.Instance);

    // ── Exec pin rules ────────────────────────────────────────────────────────

    [Fact]
    public void ExecPins_AreCompatible_WithExec()
    {
        var sut = MakeSut();
        var exec = TypeKey.Empty;
        Assert.True(sut.AreCompatible(exec, exec));
    }

    [Fact]
    public void ExecPins_NotCompatible_WithDataType()
    {
        var sut  = MakeSut();
        var exec = TypeKey.Empty;
        var data = new TypeKey(BlueprintTypeSystem.Bool);
        Assert.False(sut.AreCompatible(exec, data));
        Assert.False(sut.AreCompatible(data, exec));
    }

    // ── Data pin same-type compatibility ─────────────────────────────────────

    [Theory]
    [InlineData(BlueprintTypeSystem.Bool)]
    [InlineData(BlueprintTypeSystem.Int32)]
    [InlineData(BlueprintTypeSystem.Single)]
    [InlineData(BlueprintTypeSystem.String)]
    [InlineData(BlueprintTypeSystem.Vector2)]
    [InlineData(BlueprintTypeSystem.Vector3)]
    [InlineData(BlueprintTypeSystem.Entity)]
    public void DataPins_CompatibleBySameType(string typeId)
    {
        var sut = MakeSut();
        var key = new TypeKey(typeId);
        Assert.True(sut.AreCompatible(key, key));
    }

    // ── Incompatible types ────────────────────────────────────────────────────

    [Fact]
    public void IncompatibleTypes_NotCompatible_BoolToInt()
    {
        var sut = MakeSut();
        Assert.False(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.Bool), new TypeKey(BlueprintTypeSystem.Int32)));
    }

    [Fact]
    public void IncompatibleTypes_NotCompatible_StringToFloat()
    {
        var sut = MakeSut();
        Assert.False(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.String), new TypeKey(BlueprintTypeSystem.Single)));
    }

    [Fact]
    public void IncompatibleTypes_NotCompatible_FloatToInt()
    {
        var sut = MakeSut();
        // float → int is NOT allowed (only int → float)
        Assert.False(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.Single), new TypeKey(BlueprintTypeSystem.Int32)));
    }

    // ── Implicit cast: int → float ────────────────────────────────────────────

    [Fact]
    public void ImplicitCast_IntToFloat_IsCompatible()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.Int32), new TypeKey(BlueprintTypeSystem.Single)));
    }

    [Fact]
    public void ImplicitCast_IntToFloat_IsImplicit()
    {
        var sut = MakeSut();
        Assert.True(sut.IsImplicitCast(new TypeKey(BlueprintTypeSystem.Int32), new TypeKey(BlueprintTypeSystem.Single)));
    }

    // ── CA-07c: System.Object wildcard ───────────────────────────────────────
    // Mirrors Stage4_TypeResolve.VerifyLinkTypes' identical "typed-unknown placeholder" rule --
    // required so the FIRST wire attempt (real source type -> an unbaked ComponentForEach/ItemGet/
    // ItemCount "Collection" pin, which projects as System.Object until CA-07c's wire-bake hook
    // re-types it) is accepted by the editor validator.

    [Fact]
    public void ObjectWildcard_AnyRealTypeIntoObject_IsCompatible()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.Int32), new TypeKey("System.Object")));
        Assert.True(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.Bool),  new TypeKey("System.Object")));
        Assert.True(sut.AreCompatible(new TypeKey(BlueprintTypeSystem.Vector3), new TypeKey("System.Object")));
    }

    [Fact]
    public void ObjectWildcard_ObjectIntoAnyRealType_IsCompatible()
    {
        var sut = MakeSut();
        Assert.True(sut.AreCompatible(new TypeKey("System.Object"), new TypeKey(BlueprintTypeSystem.Int32)));
    }

    [Fact]
    public void ObjectWildcard_DoesNotSuppress_ExecVsData()
    {
        var sut = MakeSut();
        // Exec (TypeKey.Empty) is a distinct sentinel, not "System.Object" -- the wildcard rule must
        // not accidentally swallow the exec/data kind split (that's enforced upstream by
        // BlueprintLinkValidator's PinKind check, but AreCompatible itself should stay precise too).
        Assert.False(sut.AreCompatible(TypeKey.Empty, new TypeKey("System.Object")));
    }

    [Fact]
    public void ImplicitCast_SameType_IsNotImplicitCast()
    {
        var sut = MakeSut();
        // Same type is compatible but IsImplicitCast should return false (not a cast at all)
        Assert.False(sut.IsImplicitCast(new TypeKey(BlueprintTypeSystem.Single), new TypeKey(BlueprintTypeSystem.Single)));
    }

    [Fact]
    public void ImplicitCast_BoolToFloat_IsFalse()
    {
        var sut = MakeSut();
        Assert.False(sut.IsImplicitCast(new TypeKey(BlueprintTypeSystem.Bool), new TypeKey(BlueprintTypeSystem.Single)));
    }

    // ── Pin color stability ───────────────────────────────────────────────────

    [Fact]
    public void PinColor_StablePerType_Bool()
    {
        var sut = MakeSut();
        var key = new TypeKey(BlueprintTypeSystem.Bool);
        var color1 = sut.GetPinColor(key);
        var color2 = sut.GetPinColor(key);
        Assert.Equal(color1, color2);
        // Bool is red-ish
        Assert.True(color1.X > 0.4f, $"Bool pin color expected reddish, got {color1}");
    }

    [Fact]
    public void PinColor_Exec_IsWhite()
    {
        var sut = MakeSut();
        var color = sut.GetPinColor(TypeKey.Empty);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), color);
    }

    [Fact]
    public void PinColor_UnknownType_IsGrey()
    {
        var sut = MakeSut();
        var color = sut.GetPinColor(new TypeKey("Some.Unknown.Type"));
        // Grey = R ≈ G ≈ B, all around 0.8
        Assert.True(Math.Abs(color.X - color.Y) < 0.1f && Math.Abs(color.Y - color.Z) < 0.1f,
            $"Unknown type should be grey, got {color}");
    }

    [Fact]
    public void PinColor_DifferentTypes_AreDifferentColors()
    {
        var sut   = MakeSut();
        var bool_ = sut.GetPinColor(new TypeKey(BlueprintTypeSystem.Bool));
        var int_  = sut.GetPinColor(new TypeKey(BlueprintTypeSystem.Int32));
        var flt_  = sut.GetPinColor(new TypeKey(BlueprintTypeSystem.Single));
        // All three should be distinct
        Assert.NotEqual(bool_, int_);
        Assert.NotEqual(bool_, flt_);
        Assert.NotEqual(int_,  flt_);
    }

    // ── Pin shape ─────────────────────────────────────────────────────────────

    [Fact]
    public void PinShape_Exec_IsTriangle()
    {
        var sut = MakeSut();
        Assert.Equal(PinShape.Triangle, sut.GetPinShape(TypeKey.Empty, ContainerKind.Single));
    }

    [Fact]
    public void PinShape_SingleData_IsCircle()
    {
        var sut = MakeSut();
        Assert.Equal(PinShape.Circle, sut.GetPinShape(new TypeKey(BlueprintTypeSystem.Single), ContainerKind.Single));
    }

    [Fact]
    public void PinShape_ArrayData_IsDiamond()
    {
        var sut = MakeSut();
        Assert.Equal(PinShape.Diamond, sut.GetPinShape(new TypeKey(BlueprintTypeSystem.Int32), ContainerKind.Array));
    }

    [Fact]
    public void PinShape_MapData_IsSquare()
    {
        var sut = MakeSut();
        Assert.Equal(PinShape.Square, sut.GetPinShape(new TypeKey(BlueprintTypeSystem.String), ContainerKind.Map));
    }

    [Fact]
    public void PinShape_SetData_IsPentagon()
    {
        var sut = MakeSut();
        Assert.Equal(PinShape.Pentagon, sut.GetPinShape(new TypeKey(BlueprintTypeSystem.String), ContainerKind.Set));
    }

    [Fact]
    public void PinShape_StablePerType_SameCallReturnsSameShape()
    {
        var sut = MakeSut();
        var key = new TypeKey(BlueprintTypeSystem.Vector3);
        Assert.Equal(sut.GetPinShape(key, ContainerKind.Single), sut.GetPinShape(key, ContainerKind.Single));
    }

    // ── TryGetTypeInfo ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BlueprintTypeSystem.Bool,   "Boolean")]
    [InlineData(BlueprintTypeSystem.Int32,  "Integer")]
    [InlineData(BlueprintTypeSystem.Single, "Float")]
    [InlineData(BlueprintTypeSystem.String, "String")]
    [InlineData(BlueprintTypeSystem.Entity, "Entity")]
    public void TryGetTypeInfo_KnownType_ReturnsExpectedName(string typeId, string expectedName)
    {
        var sut = MakeSut();
        var got = sut.TryGetTypeInfo(new TypeKey(typeId), out var info);
        Assert.True(got);
        Assert.Equal(expectedName, info.DisplayName);
    }

    [Fact]
    public void TryGetTypeInfo_UnknownType_ReturnsFalse()
    {
        var sut = MakeSut();
        var got = sut.TryGetTypeInfo(new TypeKey("Totally.Unknown.Type"), out _);
        Assert.False(got);
    }

    // ── GetDefaultEditor ──────────────────────────────────────────────────────

    [Fact]
    public void GetDefaultEditor_WithNullRegistry_ReturnsNull()
    {
        var sut = MakeSut();
        Assert.Null(sut.GetDefaultEditor(new TypeKey(BlueprintTypeSystem.Bool)));
    }
}
