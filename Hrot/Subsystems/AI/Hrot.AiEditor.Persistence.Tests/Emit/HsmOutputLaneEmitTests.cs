using System;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>HSM-020</c> — the emitted machine declares its output lanes, so the kernel has something to arbitrate.</b>
///
/// <para>📐 <c>HsmKernelCore.ArbitrateOutputLanes</c> runs on every transition and reads
/// <c>StateDef.OutputLaneMask</c> of each region's active leaf — but until this change nothing SET that field
/// except a test fixture, so two parallel regions driving the same lane were never detected. The lane lives on
/// <c>[HsmAction(Lane = …)]</c>, which only a reflecting or Roslyn-backed caller can read; the emitter is handed
/// the answer as a resolver.</para>
///
/// <para>⚠ This project is one of the few that can see BOTH <c>HsmEmitCore</c> (netstandard2.0, no FastHSM
/// reference) and <c>CommandLane</c>, which is why the "the emitted bit means what the enum says" rail lives
/// here rather than beside the emitter.</para>
/// </summary>
public sealed class HsmOutputLaneEmitTests
{
    private static readonly Guid RootId = Guid.Parse("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid IdleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// ⚠ The DTO needs the compiler-inserted <c>__Root</c>: <c>HsmEmitCore</c> takes the state with no resolvable
    /// parent as the root and emits its CHILDREN as the top-level states. A single parentless state is therefore
    /// the root itself, has no children, and <b>emits no state at all</b> — which looks exactly like "the feature
    /// did not fire" and is how this fixture was wrong the first time.
    /// </summary>
    private static HsmAssetDto OneStateBinding(string activityFqn)
    {
        var dto = new HsmAssetDto { Name = "LaneDemo" };
        dto.States.Add(new StateNodeDto
        {
            Name            = "__Root",
            StableId        = RootId,
            ChildStableIds  = { IdleId },
        });
        dto.States.Add(new StateNodeDto
        {
            Name            = "Idle",
            StableId        = IdleId,
            ParentStableId  = RootId,
            IsInitial       = true,
            Activity        = new BehaviorActionBindingDto { MethodFqn = activityFqn },
        });
        return dto;
    }

    [Fact]
    public void TheFixtureActuallyEmitsTheState()
    {
        // ⭐ Guards every other rail here: a DoesNotContain assertion over an empty machine passes for the
        //    wrong reason.
        Assert.Contains("builder.State(\"Idle\"", HsmEmitCore.Emit(OneStateBinding("Ai.Act.Walk")));
    }

    [Fact]
    public void WithNoResolver_NothingIsEmitted_AndTheOutputIsUnchanged()
    {
        // ⭐ The compatibility guarantee: every caller that does not supply a resolver emits exactly what it
        //    emitted before, so no golden moves on account of this feature.
        string code = HsmEmitCore.Emit(OneStateBinding("Ai.Act.Walk"));

        Assert.DoesNotContain(".OutputLaneMask(", code);
    }

    [Fact]
    public void AnActionWithALane_EmitsThatLanesBit()
    {
        string code = HsmEmitCore.Emit(OneStateBinding("Ai.Act.Walk"),
                                       fqn => fqn == "Ai.Act.Walk" ? (byte)CommandLane.Navigation : null);

        Assert.Contains($".OutputLaneMask(0x{(byte)(1 << (int)CommandLane.Navigation):X2})", code);
    }

    [Fact]
    public void AnActionWithNoLane_EmitsNothing()
    {
        string code = HsmEmitCore.Emit(OneStateBinding("Ai.Act.Walk"), _ => null);

        Assert.DoesNotContain(".OutputLaneMask(", code);
    }

    [Fact]
    public void TheLanesOfEverySlot_AreOred()
    {
        var dto = OneStateBinding("Ai.Act.Walk");
        // ⚠ By NAME, not States[0] — index 0 is the compiler root, and binding the action there emits nothing.
        dto.States.Single(s => s.Name == "Idle").OnEntry =
            new BehaviorActionBindingDto { MethodFqn = "Ai.Act.Pose" };

        string code = HsmEmitCore.Emit(dto, fqn => fqn switch
        {
            "Ai.Act.Walk" => (byte)CommandLane.Navigation,
            "Ai.Act.Pose" => (byte)CommandLane.Animation,
            _             => null,
        });

        var expected = (byte)((1 << (int)CommandLane.Navigation) | (1 << (int)CommandLane.Animation));
        Assert.Contains($".OutputLaneMask(0x{expected:X2})", code);
    }

    [Fact]
    public void CommandLaneNone_IsNotALane_AndIsDropped()
    {
        // ⛔ None is 0xFF. C# would shift by 255 & 31 and set a bit that means nothing; the emitter drops it
        //    before the shift. (The builder independently drops anything >= CommandLane.Count.)
        string code = HsmEmitCore.Emit(OneStateBinding("Ai.Act.Walk"), _ => (byte)CommandLane.None);

        Assert.DoesNotContain(".OutputLaneMask(", code);
    }

    [Fact]
    public void TheEmittedCall_IsTheBuildersByteOverload()
    {
        // ⭐ Pins the SHAPE, not just the number: FastHSM's StateBuilder must carry OutputLaneMask(byte) for the
        //    generated file to compile at all. Its readable twin, OutputLanes(params CommandLane[]), is for hand
        //    authors and is not what a generator emits.
        string code = HsmEmitCore.Emit(OneStateBinding("Ai.Act.Walk"), _ => (byte)CommandLane.Gameplay);

        Assert.Contains(".OutputLaneMask(0x", code);
        Assert.DoesNotContain(".OutputLanes(", code);
    }
}
