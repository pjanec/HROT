namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Simple toast notification surface. Implement to forward to
/// <c>NodeEditor.Core.Action.IEditorIndicators.Notify</c> or similar.
/// </summary>
public interface IBreakpointNotifier
{
    void Notify(string message);
}
