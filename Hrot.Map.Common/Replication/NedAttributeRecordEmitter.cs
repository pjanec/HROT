using System;
using Hrot.NED.Messages;
using FDP.Toolkit.Replication.Patching;

namespace Hrot.Map.Common.Replication;

/// <summary>
/// Application-layer implementation of <see cref="IAttributeRecordEmitter"/> that packs
/// emitted primitive values into <see cref="AttributeRecord"/> entries and writes them
/// into a caller-supplied <see cref="Array"/> buffer.
///
/// <para>
/// Usage pattern — bind a pre-allocated buffer, compile, then read the written slice:
/// <code>
/// var buffer  = ArrayPool&lt;AttributeRecord&gt;.Shared.Rent(64);
/// var emitter = new NedAttributeRecordEmitter(buffer);
/// compiler.Compile(utf8Json, emitter);
/// var written = emitter.Written; // ReadOnlySpan&lt;AttributeRecord&gt;
/// </code>
/// </para>
/// </summary>
public sealed class NedAttributeRecordEmitter : IAttributeRecordEmitter
{
    private AttributeRecord[] _buffer;
    private int _count;

    /// <summary>Initialises the emitter bound to <paramref name="buffer"/>.</summary>
    public NedAttributeRecordEmitter(AttributeRecord[] buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _count  = 0;
    }

    /// <summary>Number of records written since the last <see cref="Reset"/>.</summary>
    public int Count => _count;

    /// <summary>A read-only view of the records written so far.</summary>
    public ReadOnlySpan<AttributeRecord> Written => _buffer.AsSpan(0, _count);

    /// <summary>Rebinds the emitter to a new buffer and resets the write cursor.</summary>
    public void Reset(AttributeRecord[] buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _count  = 0;
    }

    public void EmitInt32(ushort attributeId, int value, short subIndex1 = 0, short subIndex2 = 0)
        => Write(attributeId, subIndex1, subIndex2,
            new AttributeValueUnion { ValueType = AttributeValueType.KindInt32, IntValue = value });

    public void EmitInt64(ushort attributeId, long value, short subIndex1 = 0, short subIndex2 = 0)
        => Write(attributeId, subIndex1, subIndex2,
            new AttributeValueUnion { ValueType = AttributeValueType.KindInt64, LongValue = value });

    public void EmitFloat32(ushort attributeId, float value, short subIndex1 = 0, short subIndex2 = 0)
        => Write(attributeId, subIndex1, subIndex2,
            new AttributeValueUnion { ValueType = AttributeValueType.KindFloat32, FloatValue = value });

    public void EmitFloat64(ushort attributeId, double value, short subIndex1 = 0, short subIndex2 = 0)
        => Write(attributeId, subIndex1, subIndex2,
            new AttributeValueUnion { ValueType = AttributeValueType.KindFloat64, DoubleValue = value });

    public void EmitBool(ushort attributeId, bool value, short subIndex1 = 0, short subIndex2 = 0)
        => Write(attributeId, subIndex1, subIndex2,
            new AttributeValueUnion { ValueType = AttributeValueType.KindBool, BoolValue = value });

    public void EmitString(ushort attributeId, string? value, short subIndex1 = 0, short subIndex2 = 0)
        => Write(attributeId, subIndex1, subIndex2,
            new AttributeValueUnion { ValueType = AttributeValueType.KindString, StringValue = value });

    private void Write(ushort attributeId, short subIndex1, short subIndex2, AttributeValueUnion value)
    {
        if (_count >= _buffer.Length) return;
        _buffer[_count++] = new AttributeRecord
        {
            AttributeId = attributeId,
            SubIndex1   = subIndex1,
            SubIndex2   = subIndex2,
            Value       = value,
        };
    }
}
