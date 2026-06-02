using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// No-op <see cref="IPinDefaultValueEditorRegistry"/> for contexts where inline pin
/// default-value editors are not needed (e.g. headless Blueprint canvas).
/// </summary>
internal sealed class NullPinDefaultValueEditorRegistry : IPinDefaultValueEditorRegistry
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NullPinDefaultValueEditorRegistry Instance = new();

    private NullPinDefaultValueEditorRegistry() { }

    public void Register(TypeKey type, IPinDefaultValueEditor editor) { }
    public void RegisterFallback(IPinDefaultValueEditor editor) { }
    public IPinDefaultValueEditor? GetEditor(TypeKey type) => null;
}
