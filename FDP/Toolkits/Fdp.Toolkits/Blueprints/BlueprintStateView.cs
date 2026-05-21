using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Read-only view over a single Blueprint instance's blackboard slot.
/// Returned by BlueprintTestFixture.GetBlueprintState for test assertions.
/// </summary>
public readonly unsafe struct BlueprintStateView
{
    private readonly byte*             _slotMemory;  // pointer to start of payload
    private readonly int               _payloadSize;
    private readonly BlueprintDefinition _def;

    internal BlueprintStateView(byte* slotMemory, int payloadSize, BlueprintDefinition def)
    {
        _slotMemory  = slotMemory;
        _payloadSize = payloadSize;
        _def         = def;
    }

    /// <summary>
    /// Reads a field by name from the slot's payload using the definition's StateFields dict.
    /// Returns false if field not found or size mismatch.
    /// </summary>
    public bool TryGetField<T>(string name, out T value) where T : unmanaged
    {
        if (!_def.StateFields.TryGetValue(name, out var fd) ||
            fd.SizeBytes != Unsafe.SizeOf<T>())
        {
            value = default;
            return false;
        }
        value = Unsafe.ReadUnaligned<T>(_slotMemory + fd.OffsetBytes);
        return true;
    }

    /// <summary>Returns the raw payload as a read-only span.</summary>
    public ReadOnlySpan<byte> AsSpan()
        => new ReadOnlySpan<byte>(_slotMemory, _payloadSize);
}
