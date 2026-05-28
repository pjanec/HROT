namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Registry that maps each <see cref="AssetKind"/> to its registered
/// <see cref="IAssetComparisonSanitizer"/>. Populated at startup by each
/// subsystem host (BTree, HSM, Blueprint, Blackboard). Registered as a
/// singleton in the DI container.
/// </summary>
public sealed class SanitizerRegistry
{
    private readonly Dictionary<AssetKind, IAssetComparisonSanitizer> _sanitizers = new();

    /// <summary>
    /// Registers a sanitizer. If a sanitizer for the same <see cref="AssetKind"/>
    /// was already registered, the new registration overwrites it.
    /// </summary>
    public void Register(IAssetComparisonSanitizer sanitizer)
    {
        _sanitizers[sanitizer.TargetKind] = sanitizer;
    }

    /// <summary>
    /// Returns the sanitizer registered for <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no sanitizer has been registered for <paramref name="kind"/>.
    /// </exception>
    public IAssetComparisonSanitizer Get(AssetKind kind)
    {
        if (_sanitizers.TryGetValue(kind, out var sanitizer))
            return sanitizer;

        throw new InvalidOperationException(
            $"No comparison sanitizer registered for AssetKind.{kind}. " +
            $"Register one via SanitizerRegistry.Register().");
    }

    /// <summary>
    /// Attempts to retrieve the sanitizer for <paramref name="kind"/> without throwing.
    /// Returns true and sets <paramref name="sanitizer"/> when found;
    /// returns false and null when not registered.
    /// </summary>
    public bool TryGet(AssetKind kind, out IAssetComparisonSanitizer? sanitizer)
    {
        return _sanitizers.TryGetValue(kind, out sanitizer);
    }
}
