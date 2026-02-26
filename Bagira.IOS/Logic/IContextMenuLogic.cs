using Bagira.BDC.SSTM;

namespace Bagira.IOS.Logic;

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
    /// change. Computes and pushes the correct <see cref="ContextActionsUpdate"/>
    /// for the new selection.
    /// </summary>
    void OnSelectionChanged(SelectionChangedEvent evt);

    /// <summary>
    /// Called when the IG user invokes an action from the context menu.
    /// Fires <see cref="ActionInvoked"/>.
    /// </summary>
    void OnActionInvoked(ContextActionInvoked evt);

    /// <summary>
    /// Raised when the IG invokes a context action so that other services
    /// (e.g. mission editor) can react.
    /// </summary>
    event Action<ContextActionInvoked> ActionInvoked;
}
