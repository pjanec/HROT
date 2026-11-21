using System;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

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
    private SearchPredicateMode _mode = SearchPredicateMode.Component;
    private SearchPredicateDto _currentDto;

    /// <summary>The currently active predicate mode.</summary>
    public SearchPredicateMode CurrentMode => _mode;

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
        _currentDto = PredicateModes.CreateDefault(_mode);
    }

    /// <summary>
    /// Switches the mode and replaces the current DTO with a blank instance of the
    /// corresponding subtype. Previous edits are discarded.
    /// </summary>
    public void SwitchMode(SearchPredicateMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _currentDto = PredicateModes.CreateDefault(mode);
    }

    /// <summary>
    /// Loads an existing breakpoint's condition into the builder.
    /// Infers and sets <see cref="CurrentMode"/> from the DTO type.
    /// </summary>
    public void LoadBreakpoint(Breakpoint bp)
    {
        if (bp.Condition is null)
        {
            _mode = SearchPredicateMode.Component;
            _currentDto = PredicateModes.CreateDefault(_mode);
            return;
        }

        _currentDto = bp.Condition;
        _mode = PredicateModes.ModeOf(bp.Condition);
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
}
