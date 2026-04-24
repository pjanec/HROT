using StructEdit.Core;

namespace StructEdit.Reflection;

/// <summary>
/// Internal implementation of <see cref="IEditSession"/>.
/// Owns the edit buffer for the duration of the session.
/// </summary>
internal sealed class EditSession : IEditSession
{
    private readonly IEditBuffer _buffer;
    private readonly IEditDocumentBuilder _builder;
    private readonly EditScope _scope;
    private readonly EditContext? _context;
    private readonly IComponentValidator? _validator;
    private EditDocument _document;
    private EditRebuildState _rebuildState = EditRebuildState.Stable;
    private bool _disposed;

    internal EditSession(
        IEditBuffer buffer,
        IEditDocumentBuilder builder,
        EditScope scope,
        EditContext? context,
        IComponentValidator? validator,
        EditDocument initialDocument)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _context = context;
        _validator = validator;
        _document = initialDocument ?? throw new ArgumentNullException(nameof(initialDocument));
    }

    // ── IEditSession ───────────────────────────────────────────────────────

    public EditDocument Document
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _document;
        }
    }

    public bool IsDirty => _buffer.IsDirty;

    public EditRebuildState RebuildState => _rebuildState;

    public void MarkStructuralChange() => _rebuildState = EditRebuildState.RebuildRequired;

    public void RebuildDocument()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _document = _builder.Build(_buffer, _buffer.ComponentType, _scope, _context);
        _rebuildState = EditRebuildState.Stable;
    }

    public ValidationResult Validate()
    {
        if (_validator == null) return ValidationResult.Ok();
        return _validator.Validate(new EditValidationContext
        {
            ComponentType = _buffer.ComponentType,
            Buffer = _buffer,
            Scope = _scope,
        });
    }

    public object Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = Validate();
        if (!result.IsValid) throw new EditValidationException(result);
        return _buffer.Box();
    }

    /// <inheritdoc/>
    /// <remarks>No-op. The buffer is discarded when <see cref="Dispose"/> is called.</remarks>
    public void Cancel() { }

    public void Dispose()
    {
        if (_disposed) return;
        _buffer.Dispose();
        _disposed = true;
    }
}
