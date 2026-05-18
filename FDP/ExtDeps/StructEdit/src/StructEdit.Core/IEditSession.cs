namespace StructEdit.Core;

/// <summary>
/// Consumer-facing handle to an active edit session.
/// Provides access to the instruction tree, dirty tracking, validation, and commit/cancel.
/// </summary>
public interface IEditSession : IDisposable
{
    /// <summary>
    /// The current instruction tree.
    /// Throws <see cref="ObjectDisposedException"/> after <see cref="IDisposable.Dispose"/>.
    /// </summary>
    EditDocument Document { get; }

    /// <summary>True if any binding has written to the edit buffer.</summary>
    bool IsDirty { get; }

    /// <summary>Indicates whether the document tree needs to be rebuilt.</summary>
    EditRebuildState RebuildState { get; }

    /// <summary>
    /// Marks the session as requiring a document rebuild (e.g. after a discriminator change).
    /// Sets <see cref="RebuildState"/> to <see cref="EditRebuildState.RebuildRequired"/>.
    /// </summary>
    void MarkStructuralChange();

    /// <summary>
    /// Rebuilds the <see cref="Document"/> tree from the current buffer state without
    /// discarding the buffer. Resets <see cref="RebuildState"/> to <see cref="EditRebuildState.Stable"/>.
    /// </summary>
    void RebuildDocument();

    /// <summary>
    /// Validates the full edit buffer against any registered <see cref="IComponentValidator"/>.
    /// Returns <see cref="ValidationResult.Ok()"/> when no validator is registered.
    /// </summary>
    ValidationResult Validate();

    /// <summary>
    /// Calls <see cref="Validate()"/>; throws <see cref="EditValidationException"/> if invalid.
    /// Returns the boxed replacement component on success. Does NOT dispose the session.
    /// </summary>
    object Commit();

    /// <summary>
    /// Semantic no-op. The edit buffer is discarded when <see cref="IDisposable.Dispose"/> is called.
    /// The original component is unmodified.
    /// </summary>
    void Cancel();
}
