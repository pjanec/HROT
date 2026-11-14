using System.Linq;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐ <c>CE-3182</c> — the ONE binding-target resolver (<see cref="BindingTargets"/>) and the path rules (<see cref="BindingPath"/>)
/// every emitter and editor site now shares. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5d. The generator rails
/// (<c>SharedAiBindingCompilesTests.CE3182_*</c>) hold the same offsets against the CLR.
/// </summary>
public sealed class BindingTargetsTests
{
    private static readonly BTreeBlackboardPackHelper.PackedField[] Packed =
    {
        new("sentinel", "Probe.P", 0, 8),
        new("cfg",      "Probe.Cfg", 8, 32),
    };

    /// <summary>Cfg { long Head; Walk walk (16); } · Walk { int a; float x (4); }</summary>
    private static FieldLayout? Fields(string type, string field) => (type, field) switch
    {
        ("Probe.Cfg", "walk")  => new FieldLayout(16, 8, "Probe.Walk"),
        ("Probe.Walk", "x")    => new FieldLayout(4, 4, "System.Single"),
        _                      => null,
    };

    [Fact]
    public void AVariable_ResolvesToItself_AndAFieldPath_ToTheVariablesOffsetPlusEachFieldsOffset()
    {
        var t = BindingTargets.For(Packed, Fields);

        t.TryResolve("cfg", out var v).Should().BeTrue();
        v.ByteOffset.Should().Be(8);

        t.TryResolve("cfg.walk", out var walk).Should().BeTrue();
        (walk.Name, walk.ByteOffset, walk.ByteSize, walk.TypeId).Should().Be(("cfg.walk", 24, 8, "Probe.Walk"));

        t.TryResolve("cfg.walk.x", out var x).Should().BeTrue();
        (x.ByteOffset, x.TypeId).Should().Be((28, "System.Single"));
    }

    [Fact]
    public void AnythingUnresolvable_FailsClosed_NeverAGuessedOffset()
    {
        var t = BindingTargets.For(Packed, Fields);
        t.TryResolve("cfg.nope", out _).Should().BeFalse();
        t.TryResolve("nope.walk", out _).Should().BeFalse();
        t.TryResolve("cfg.", out _).Should().BeFalse();
        t.TryResolve(null, out _).Should().BeFalse();
        BindingTargets.For(Packed).TryResolve("cfg.walk", out _).Should().BeFalse("no field layouts ⇒ whole variables only");
    }

    [Fact]
    public void Offsets_HoldEveryVariable_AndOnlyTheBoundPathsThatResolve()
    {
        var map = BindingTargets.For(Packed, Fields).Offsets(new[] { "cfg.walk", "cfg.nope", null, "cfg" });
        map.Should().ContainKeys("sentinel", "cfg", "cfg.walk").And.NotContainKey("cfg.nope");
        map["cfg.walk"].Should().Be(24);
    }

    [Fact]
    public void BindingPath_SplitsAtTheFirstDot_AndRetargetsTheRootOnly()
    {
        (BindingPath.Root("cfg.walk.x"), BindingPath.Field("cfg.walk.x")).Should().Be(("cfg", "walk.x"));
        (BindingPath.Root("cfg"), BindingPath.Field("cfg")).Should().Be(("cfg", (string?)null));
        BindingPath.Targets("Cfg.walk", "cfg").Should().BeTrue("case-insensitive, as HSM's IsExpressionTargetOf always was");
        BindingPath.Targets("cfgOther.walk", "cfg").Should().BeFalse();
        BindingPath.Retarget("cfg.walk", "cfg", "conf").Should().Be("conf.walk");
        BindingPath.Retarget("cfg", "cfg", "conf").Should().Be("conf");
        BindingPath.Retarget("other.walk", "cfg", "conf").Should().Be("other.walk");
        BindingPath.Retarget(null, "cfg", "conf").Should().BeNull();
    }
}
