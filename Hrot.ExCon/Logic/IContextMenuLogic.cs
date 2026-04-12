using Hrot.Core.Network;

namespace Hrot.ExCon.Logic;

/// <summary>
/// The active menu-building strategy.
/// Each value corresponds to a different operator role/mode.
/// </summary>
public enum MenuStrategy
{
    Standard,
    Admin,
    DamageControl,
    Logistics
}

/// <summary>
/// Manages the context menus pushed to the IG whenever the selection changes
/// or the active strategy is switched. Uses the Strategy pattern: the current
/// <see cref="MenuStrategy"/> determines which menu items are built.
/// </summary>
public interface IContextMenuLogic
{
    /// <summary>Gets the currently active menu-building strategy.</summary>
    MenuStrategy CurrentStrategy { get; }

    /// <summary>Switches the active strategy. Takes effect on the next push.</summary>
    void SetStrategy(MenuStrategy strategy);

    /// <summary>
    /// Called by the network ingress layer when the IG reports a selection
    /// change. Computes and pushes the correct context actions for the new selection.
    /// </summary>
    /// <param name="evt">The incoming selection-change event.</param>
    /// <param name="isEntityPending">
    /// Optional predicate that returns <c>true</c> when a given entity ID is
    /// currently in the Two-ACK pending state (Phase-1 received, Phase-2 not yet
    /// arrived). When the selected entity is pending, an empty menu is pushed
    /// so the operator cannot interact with a half-baked entity.
    /// Pass <c>null</c> to skip the check (e.g. in unit tests).
    /// </param>
    void OnSelectionChanged(SelectionChangedEventDto evt, Func<int, bool>? isEntityPending = null);

    /// <summary>
    /// Called when the IG user invokes an action from the context menu.
    /// Fires <see cref="ActionInvoked"/>.
    /// </summary>
    void OnActionInvoked(ContextActionInvokedDto evt);

    /// <summary>
    /// Raised when the IG invokes a context action so that other services
    /// (e.g. mission editor) can react.
    /// </summary>
    event Action<ContextActionInvokedDto> ActionInvoked;
}
