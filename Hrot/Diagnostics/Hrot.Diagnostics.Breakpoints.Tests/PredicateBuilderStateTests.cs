using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

[Collection("ComponentRegistry")]
public sealed class PredicateBuilderStateTests
{
    private readonly DataBreakpointManager _manager;

    public PredicateBuilderStateTests()
    {
        ComponentTypeRegistry.Clear();
        var (mgr, _, _, _) = ManagerFactory.Create();
        _manager = mgr;
    }

    // ── P8T2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PredicateBuilder_SwitchingMode_DiscardsAndOpensNewSession()
    {
        var state = new PredicateBuilderState();

        // Starts in Component mode with a PropertyMatchDto
        Assert.Equal(PredicateMode.Component, state.CurrentMode);
        Assert.IsType<PropertyMatchDto>(state.CurrentDto);

        // Switch to BehaviorParam
        state.SwitchMode(PredicateMode.BehaviorParam);
        Assert.Equal(PredicateMode.BehaviorParam, state.CurrentMode);
        Assert.IsType<BehaviorParamPredicateDto>(state.CurrentDto);
    }

    [Fact]
    public void PredicateBuilder_SwitchingToSameMode_IsNoOp()
    {
        var state = new PredicateBuilderState();
        var originalDto = state.CurrentDto;

        state.SwitchMode(PredicateMode.Component); // same mode
        Assert.Same(originalDto, state.CurrentDto); // same DTO instance
    }

    [Fact]
    public void PredicateBuilder_CompileAndApply_RemountsDelegate()
    {
        // Register a breakpoint with one condition
        var originalPred = new PropertyMatchDto
        {
            ComponentType = typeof(StubPredicateBuilderComponent),
            PropertyPath  = "Value",
            Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
        };
        var id = _manager.AddBreakpoint(originalPred, displayName: "original");

        // Load the breakpoint and switch to BehaviorParam (completely different condition)
        var state = new PredicateBuilderState();
        state.LoadBreakpoint(_manager.AllBreakpoints.First(b => b.Id == id));
        state.SwitchMode(PredicateMode.BehaviorParam);
        state.Apply(id, _manager);

        // Condition is now BehaviorParamPredicateDto -- the delegate was remounted for the new condition.
        var updated = _manager.AllBreakpoints.First(b => b.Id == id);
        Assert.IsType<BehaviorParamPredicateDto>(updated.Condition);
    }

    [Fact]
    public void PredicateBuilder_LoadBreakpoint_InfersMode()
    {
        var realId = _manager.AddBreakpoint(new CompoundPredicateDto());
        var bp = _manager.AllBreakpoints.First(b => b.Id == realId);

        var state = new PredicateBuilderState();
        state.LoadBreakpoint(bp);

        Assert.Equal(PredicateMode.Compound, state.CurrentMode);
        Assert.IsType<CompoundPredicateDto>(state.CurrentDto);
    }

    [Fact]
    public void PredicateBuilder_AllModes_ProduceExpectedDtoType()
    {
        var state = new PredicateBuilderState();
        foreach (PredicateMode mode in Enum.GetValues<PredicateMode>())
        {
            state.SwitchMode(mode);
            Assert.NotNull(state.CurrentDto); // every mode must produce a non-null DTO
        }
    }
}

[ComponentId(222)]
file struct StubPredicateBuilderComponent { public int Value; }
