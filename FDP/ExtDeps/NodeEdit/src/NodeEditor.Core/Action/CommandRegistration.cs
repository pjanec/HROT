namespace NodeEditor.Core.Action;

/// <summary>
/// Fluent helper for building and registering <see cref="EditorCommandDescriptor"/> entries
/// on an <see cref="EditorCommandsImpl"/> instance.
/// </summary>
public sealed class CommandRegistration
{
    private readonly EditorCommandsImpl _impl;

    /// <summary>Create a registration helper targeting the given commands implementation.</summary>
    public CommandRegistration(EditorCommandsImpl impl)
    {
        _impl = impl;
    }

    /// <summary>
    /// Register a command with the given id, display name, category, and action.
    /// </summary>
    public CommandRegistration Add(
        string                              id,
        string                              displayName,
        string?                             category,
        System.Action<EditorCommandContext> action,
        Func<bool>?                         isEnabled   = null,
        string?                             description = null,
        string?                             iconKey     = null,
        KeyBinding?                         defaultKey  = null,
        Func<bool>?                         isChecked   = null)
    {
        var descriptor = new EditorCommandDescriptor(
            id, displayName, category, description, iconKey,
            defaultKey,
            IsEnabled: isEnabled ?? (() => true),
            IsChecked: isChecked);
        _impl.Register(descriptor, action);
        return this;
    }
}
