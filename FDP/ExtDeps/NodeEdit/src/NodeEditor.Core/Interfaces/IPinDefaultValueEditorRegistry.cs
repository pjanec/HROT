using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Registry of type-key → editor mappings for inline pin defaults.
/// The editor library auto-registers built-in editors at init time.
/// Hosts can override any by re-registering.
/// </summary>
public interface IPinDefaultValueEditorRegistry
{
    /// <summary>Register or replace an editor for the given type.</summary>
    void Register(TypeKey type, IPinDefaultValueEditor editor);

    /// <summary>Register a fallback used when no type-specific editor matches.</summary>
    void RegisterFallback(IPinDefaultValueEditor editor);

    /// <summary>Look up the editor for a type, or null if none.</summary>
    IPinDefaultValueEditor? GetEditor(TypeKey type);
}
