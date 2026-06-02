using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>No-op <see cref="IPinDefaultValueEditorRegistry"/> used in headless tests.</summary>
internal sealed class NullPinDefaultValueEditorRegistry : IPinDefaultValueEditorRegistry
{
    public static readonly NullPinDefaultValueEditorRegistry Instance = new();

    public void Register(TypeKey type, IPinDefaultValueEditor editor) { }
    public void RegisterFallback(IPinDefaultValueEditor editor) { }
    public IPinDefaultValueEditor? GetEditor(TypeKey type) => null;
}
