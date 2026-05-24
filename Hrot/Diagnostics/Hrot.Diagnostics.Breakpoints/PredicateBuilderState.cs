using System;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Predicate mode for the Data Breakpoint Manager's Details Inspector.
/// Determines which root <see cref="SearchPredicateDto"/> subtype is used.
/// </summary>
public enum PredicateMode
{
    Component,
    Event,
    Lifecycle,
    Spatial,
    Structural,
    Compound,
    BehaviorParam,
    BlueprintVariable,
    TraceBufferScan,
}

/// <summary>
/// Pure-logic state for the Predicate Builder panel.
/// Extracted from the ImGui panel so it can be unit-tested without an ImGui context.
///
/// Responsibilities:
///   - Tracks the current <see cref="PredicateMode"/> and the corresponding root DTO.
///   - <see cref="SwitchMode"/> discards the current DTO and creates a blank replacement.
///   - <see cref="Apply"/> calls <see cref="IDataBreakpointManager.UpdateCondition"/>
///     to remount the newly configured predicate.
/// </summary>
public sealed class PredicateBuilderState
{
    private PredicateMode _mode = PredicateMode.Component;
    private SearchPredicateDto _currentDto;

    /// <summary>The currently active predicate mode.</summary>
    public PredicateMode CurrentMode => _mode;

    /// <summary>
    /// The currently configured predicate DTO.
    /// May be modified in-place by the StructEdit session before <see cref="Apply"/> is called.
    /// </summary>
    public SearchPredicateDto CurrentDto
    {
        get  => _currentDto;
        set  => _currentDto = value ?? throw new ArgumentNullException(nameof(value));
    }

    public PredicateBuilderState()
    {
        _currentDto = CreateDefaultDto(_mode);
    }

    /// <summary>
    /// Switches the mode and replaces the current DTO with a blank instance of the
    /// corresponding subtype. Previous edits are discarded.
    /// </summary>
    public void SwitchMode(PredicateMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _currentDto = CreateDefaultDto(mode);
    }

    /// <summary>
    /// Loads an existing breakpoint's condition into the builder.
    /// Infers and sets <see cref="CurrentMode"/> from the DTO type.
    /// </summary>
    public void LoadBreakpoint(Breakpoint bp)
    {
        if (bp.Condition is null)
        {
            _mode = PredicateMode.Component;
            _currentDto = CreateDefaultDto(_mode);
            return;
        }

        _currentDto = bp.Condition;
        _mode = InferMode(bp.Condition);
    }

    /// <summary>
    /// Calls <see cref="IDataBreakpointManager.UpdateCondition"/> with the current DTO,
    /// which triggers a remount of the compiled delegate in the manager.
    /// </summary>
    public void Apply(BreakpointId id, IDataBreakpointManager manager)
    {
        if (manager is null) throw new ArgumentNullException(nameof(manager));
        manager.UpdateCondition(id, _currentDto);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SearchPredicateDto CreateDefaultDto(PredicateMode mode) => mode switch
    {
        PredicateMode.Component       => new PropertyMatchDto(),
        PredicateMode.Event           => new TransientEventPredicateDto(),
        PredicateMode.Lifecycle       => new LifecyclePredicateDto(),
        PredicateMode.Spatial         => new SpatialBoundingPredicateDto(),
        PredicateMode.Structural      => new StructuralPredicateDto(),
        PredicateMode.Compound        => new CompoundPredicateDto(),
        PredicateMode.BehaviorParam   => new BehaviorParamPredicateDto(),
        PredicateMode.BlueprintVariable => new BlueprintVariablePredicateDto(),
        PredicateMode.TraceBufferScan => new TraceBufferScanPredicateDto(),
        _                             => new PropertyMatchDto(),
    };

    private static PredicateMode InferMode(SearchPredicateDto dto) => dto switch
    {
        PropertyMatchDto            => PredicateMode.Component,
        TransientEventPredicateDto  => PredicateMode.Event,
        LifecyclePredicateDto       => PredicateMode.Lifecycle,
        SpatialBoundingPredicateDto => PredicateMode.Spatial,
        StructuralPredicateDto      => PredicateMode.Structural,
        CompoundPredicateDto        => PredicateMode.Compound,
        BehaviorParamPredicateDto   => PredicateMode.BehaviorParam,
        BlueprintVariablePredicateDto => PredicateMode.BlueprintVariable,
        TraceBufferScanPredicateDto => PredicateMode.TraceBufferScan,
        _                           => PredicateMode.Component,
    };
}
