using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using NodeEditor.UI.MiniEditors;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Thin <see cref="IPinDefaultValueEditorRegistry"/> wrapper that intercepts
/// <see cref="GetEditor"/> calls for Blueprint enum TypeKeys (those whose
/// <see cref="TypeKey.Id"/> starts with <c>"global::"</c>) and returns a shared
/// <see cref="EnumPinEditor"/> backed by the supplied <see cref="IEnumValueProvider"/>.
/// All other TypeKeys are delegated to the wrapped inner registry.
/// <para>
/// Rationale: <see cref="PinDefaultValueEditorRegistry"/> requires exact TypeKey registration
/// and cannot pattern-match "any global:: type".  Rather than registering one
/// <see cref="EnumPinEditor"/> per discovered enum (which would require up-front scanning and
/// re-registration on assembly reload), this wrapper intercepts at lookup time — O(1), lazy,
/// and assembly-reload-safe.
/// </para>
/// <para>
/// Wired in <see cref="BlueprintDocumentFactory.Build"/> after
/// <c>PinDefaultValueEditorRegistry.CreateWithBuiltins()</c> so the framework factory is
/// never touched (contract: DO NOT edit <c>CreateWithBuiltins</c>).
/// </para>
/// </summary>
internal sealed class EnumSentinelPinEditorRegistry : IPinDefaultValueEditorRegistry
{
    private const string GlobalPrefix = "global::";

    private readonly IPinDefaultValueEditorRegistry _inner;
    private readonly EnumPinEditor _enumEditor;

    /// <param name="inner">
    ///   The wrapped registry (e.g. <see cref="PinDefaultValueEditorRegistry"/> from
    ///   <c>CreateWithBuiltins()</c> with FixedString entries added).
    /// </param>
    /// <param name="provider">
    ///   The enum value provider injected into every <see cref="EnumPinEditor"/> returned for
    ///   <c>global::</c>-prefixed TypeKeys.
    /// </param>
    public EnumSentinelPinEditorRegistry(
        IPinDefaultValueEditorRegistry inner,
        IEnumValueProvider             provider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(provider);
        _inner      = inner;
        _enumEditor = new EnumPinEditor(provider);
    }

    // ── IPinDefaultValueEditorRegistry ───────────────────────────────────────

    /// <inheritdoc/>
    public void Register(TypeKey type, IPinDefaultValueEditor editor)
        => _inner.Register(type, editor);

    /// <inheritdoc/>
    public void RegisterFallback(IPinDefaultValueEditor editor)
        => _inner.RegisterFallback(editor);

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the shared <see cref="EnumPinEditor"/> for any TypeKey whose
    /// <see cref="TypeKey.Id"/> starts with <c>"global::"</c>.  All other TypeKeys are
    /// delegated to the inner registry.
    /// </remarks>
    public IPinDefaultValueEditor? GetEditor(TypeKey type)
    {
        if (!string.IsNullOrEmpty(type.Id)
            && type.Id.StartsWith(GlobalPrefix, StringComparison.Ordinal))
            return _enumEditor;

        return _inner.GetEditor(type);
    }
}
