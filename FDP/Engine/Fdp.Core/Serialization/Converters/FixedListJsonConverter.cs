using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core.Serialization.Converters;

/// <summary>
/// FC-3b (Q#21-C3/C1) — JSON support for the canonical fixed-list wrapper pattern
/// (<see cref="FixedListShape"/>): the designer authors a PLAIN ARRAY —
/// <c>"Waypoints": [3, 7]</c> — never a <c>Count</c>:
/// <list type="bullet">
///   <item><b>Read:</b> <c>Count</c> = array length, clamped to <c>[0, N]</c> (elements beyond
///   capacity are dropped — the authoring-time twin of BP1504); the unused tail stays
///   <c>default</c> bytes (G6). <c>null</c> reads as an empty list.</item>
///   <item><b>Write:</b> emits only the used window (<c>min(Count, N)</c> elements, with a
///   defensive floor at 0 — a corrupt negative Count writes <c>[]</c>).</item>
///   <item><b>Elements</b> recurse through the ENCLOSING options, so element-type support is
///   inherited: primitives, vectors/quaternions (compact array form), FixedStrings, enums
///   (strict string form), and arbitrary unmanaged structs (<c>IncludeFields</c>) all work
///   without list-specific code. <c>Entity</c> elements round-trip structurally; authoring a
///   non-null handle is meaningless (runtime-assigned) — author <c>Entity.Null</c> only.</item>
/// </list>
/// Registered in <see cref="FdpJsonOptionsRegistry"/> (both singletons), so behavior-JSON
/// Params defaults, scenario save/load, and diagnostic dumps all share one wire format.
/// </summary>
public sealed class FixedListJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => FixedListShape.TryGet(typeToConvert, out _, out _);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!FixedListShape.TryGet(typeToConvert, out var elemType, out var bufType,
                out int capacity, out var countField, out var bufferField))
            throw new InvalidOperationException(
                $"'{typeToConvert}' is not a fixed-list wrapper (CanConvert should have rejected it).");

        var converterType = typeof(FixedListConverter<,,>)
            .MakeGenericType(typeToConvert, bufType, elemType);
        return (JsonConverter)Activator.CreateInstance(
            converterType, capacity, countField, bufferField)!;
    }

    private sealed class FixedListConverter<TList, TBuf, TElem> : JsonConverter<TList>
        where TList : struct
        where TBuf : struct
        where TElem : struct
    {
        private readonly int _capacity;
        private readonly FieldInfo _countField;
        private readonly FieldInfo _bufferField;

        public FixedListConverter(int capacity, FieldInfo countField, FieldInfo bufferField)
        {
            _capacity    = capacity;
            _countField  = countField;
            _bufferField = bufferField;
        }

        public override TList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return default;                                    // empty list (Count 0, zeroed)

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException(
                    $"Fixed list '{typeof(TList).Name}' must be authored as a JSON array " +
                    $"(e.g. [1, 2]); got {reader.TokenType}.");

            // Fill the buffer's used prefix; default-init means the tail stays zero (G6).
            TBuf buf = default;
            var span = MemoryMarshal.CreateSpan(ref Unsafe.As<TBuf, TElem>(ref buf), _capacity);
            int count = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var elem = JsonSerializer.Deserialize<TElem>(ref reader, options);
                if (count < _capacity)
                    span[count++] = elem;                          // beyond capacity: clamped (dropped)
            }

            // Assemble via boxed field writes — STJ cannot touch the [InlineArray] backing
            // field, but a whole-buffer struct assignment is a flat copy.
            object boxed = default(TList);
            _countField.SetValue(boxed, count);
            _bufferField.SetValue(boxed, buf);
            return (TList)boxed;
        }

        public override void Write(Utf8JsonWriter writer, TList value, JsonSerializerOptions options)
        {
            object boxed = value;
            int count = (int)_countField.GetValue(boxed)!;
            var buf = (TBuf)_bufferField.GetValue(boxed)!;
            var span = MemoryMarshal.CreateSpan(ref Unsafe.As<TBuf, TElem>(ref buf), _capacity);

            int used = Math.Min(Math.Max(count, 0), _capacity);    // F2: corrupt Count never overreads
            writer.WriteStartArray();
            for (int i = 0; i < used; i++)
                JsonSerializer.Serialize(writer, span[i], options);
            writer.WriteEndArray();
        }
    }
}
