namespace Hrot.UI.Common.Models;

/// <summary>
/// The result returned when a mission commit or control-command request resolves.
/// </summary>
/// <param name="Success">Whether the server accepted and applied the change.</param>
/// <param name="NewVersion">The new optimistic-lock version after a successful commit; 0 on failure.</param>
/// <param name="ErrorMessage">Human-readable error description; <c>null</c> on success.</param>
public record MissionCommitResult(bool Success, long NewVersion, string? ErrorMessage = null);
