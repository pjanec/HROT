using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using Hrot.NED.Messages;

namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Zero-allocation edge compiler that converts a UTF-8 JSON attribute patch into a
/// sequence of <see cref="AttributeRecord"/>s written to a caller-supplied
/// <see cref="Span{T}"/> buffer.
///
/// <para>
/// Handles three JSON shapes without heap allocation:
/// <list type="bullet">
///   <item>Flat dotted keys: <c>{ "GeoPosition.Latitude": 32.0 }</c></item>
///   <item>Nested objects: <c>{ "GeoPosition": { "Latitude": 32.0 } }</c></item>
///   <item>Array-indexed nested objects: <c>{ "Weapon": { "2": { "Ammo": 5 } } }</c>
///         — integer string keys are captured as <see cref="AttributeRecord.SubIndex1"/> /
///         <see cref="AttributeRecord.SubIndex2"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// The routing table is fixed at build time (see <see cref="JsonToRecordCompilerBuilder"/>).
/// Thread-safe and allocation-free on the hot path for non-string value types.
/// </para>
/// </summary>
public sealed class JsonToRecordCompiler
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int MaxDepth = 16;

    private static ReadOnlySpan<byte> WildcardBytes => "*"u8;
    private static ReadOnlySpan<byte> SeparatorBytes => "."u8;

    // ── State ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Concrete routing table — avoids virtual dispatch on the hot path
    /// compared to <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
    /// </summary>
    private readonly Dictionary<ulong, EdgeSchemaEntry> _routes;

    /// <summary>
    /// Per-instance string intern pool for <see cref="AttributeValueType.KindString"/> values.
    /// High-cardinality domains (e.g. faction enums) send the same strings repeatedly;
    /// interning returns the cached reference and lets the GC skip short-lived duplicates.
    /// The pool has no hard capacity limit — the string domain is bounded by the attribute schema.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _stringPool =
        new(StringComparer.Ordinal);

    internal JsonToRecordCompiler(Dictionary<ulong, EdgeSchemaEntry> routes)
    {
        _routes = routes;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles all JSON attribute values in <paramref name="utf8Json"/> to
    /// <see cref="AttributeRecord"/>s and writes them into <paramref name="output"/>.
    /// </summary>
    /// <param name="utf8Json">Valid UTF-8 JSON object. Empty input returns 0.</param>
    /// <param name="output">Caller-supplied output buffer.  Use <c>stackalloc</c> or an
    /// <c>ArrayPool</c>-rented buffer to keep the call site allocation-free.</param>
    /// <returns>Number of records written to <paramref name="output"/>.</returns>
    /// <remarks>
    /// If <paramref name="output"/> has fewer slots than the number of matching paths
    /// in <paramref name="utf8Json"/>, records are emitted up to the buffer length;
    /// the remainder are silently dropped.
    /// </remarks>
    public int Compile(ReadOnlySpan<byte> utf8Json, Span<AttributeRecord> output)
    {
        if (utf8Json.IsEmpty)
            return 0;

        var reader = new Utf8JsonReader(utf8Json);

        // ── Stack-allocated state machine ─────────────────────────────────────
        // contextStack[d] = accumulated FNV-1a hash context at depth d.
        // contextStack[0] = FnvOffset (root level parent hash).
        Span<ulong> contextStack      = stackalloc ulong[MaxDepth + 1];
        // Per-depth flag: was a numeric index key consumed at this depth.
        Span<byte>  hadNumericAtDepth = stackalloc byte[MaxDepth + 1];

        contextStack[0] = JsonAttributeCompiler.FnvOffset;
        int    depth        = 0;
        ulong  currentLeafHash = JsonAttributeCompiler.FnvOffset;
        int    outputCount  = 0;

        // Sub-index accumulation: numeric key strings become SubIndex1/SubIndex2.
        short  subIndex1    = 0;
        short  subIndex2    = 0;
        int    numericCount = 0; // how many numeric keys are active in current path

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    // Push current leaf hash as next depth's parent context.
                    if (depth < MaxDepth)
                    {
                        contextStack[depth + 1] = currentLeafHash;
                        depth++;
                    }
                    break;

                case JsonTokenType.EndObject:
                    if (depth > 0)
                    {
                        // Pop numeric index if this depth had one.
                        if (hadNumericAtDepth[depth] != 0)
                        {
                            hadNumericAtDepth[depth] = 0;
                            numericCount--;
                            if (numericCount == 0)
                            {
                                subIndex1 = 0;
                                subIndex2 = 0;
                            }
                            else if (numericCount == 1)
                            {
                                subIndex2 = 0;
                            }
                        }
                        depth--;
                    }
                    break;

                case JsonTokenType.PropertyName:
                {
                    ReadOnlySpan<byte> nameBytes = reader.ValueSpan;

                    if (IsAllDigits(nameBytes))
                    {
                        // Numeric index key → capture as sub-index, hash with wildcard.
                        short indexVal = (short)ParseInt(nameBytes);
                        if (numericCount == 0)
                            subIndex1 = indexVal;
                        else if (numericCount == 1)
                            subIndex2 = indexVal;

                        numericCount++;
                        hadNumericAtDepth[depth] = 1;

                        currentLeafHash = JsonAttributeCompiler.HashBytes(
                            JsonAttributeCompiler.HashBytes(contextStack[depth], SeparatorBytes),
                            WildcardBytes);
                    }
                    else
                    {
                        // Named property: hash separator then the UTF-8 name bytes.
                        currentLeafHash = JsonAttributeCompiler.HashBytes(
                            JsonAttributeCompiler.HashBytes(contextStack[depth], SeparatorBytes),
                            nameBytes);
                    }
                    break;
                }

                // Primitive leaf value tokens — attempt dispatch.
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                {
                    if (outputCount >= output.Length)
                        break;  // Buffer full — silently drop remaining matches.

                    if (_routes.TryGetValue(currentLeafHash, out var entry))
                    {
                        output[outputCount++] = new AttributeRecord
                        {
                            AttributeId = entry.AttributeId,
                            SubIndex1   = subIndex1,
                            SubIndex2   = subIndex2,
                            Value       = ExtractValue(ref reader, entry.ExpectedType),
                        };
                    }
                    break;
                }
            }
        }

        return outputCount;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a typed <see cref="AttributeValueUnion"/> from the current
    /// <see cref="Utf8JsonReader"/> position based on <paramref name="expectedType"/>.
    /// For <see cref="AttributeValueType.KindString"/>, the raw string is interned
    /// through <see cref="_stringPool"/> to eliminate duplicate allocations on repeated
    /// payloads (e.g. faction enum strings).
    /// </summary>
    private AttributeValueUnion ExtractValue(ref Utf8JsonReader reader, AttributeValueType expectedType)
    {
        return expectedType switch
        {
            AttributeValueType.KindInt32   => new AttributeValueUnion
                { ValueType = AttributeValueType.KindInt32,   IntValue    = reader.GetInt32()  },
            AttributeValueType.KindInt64   => new AttributeValueUnion
                { ValueType = AttributeValueType.KindInt64,   LongValue   = reader.GetInt64()  },
            AttributeValueType.KindFloat32 => new AttributeValueUnion
                { ValueType = AttributeValueType.KindFloat32, FloatValue  = reader.GetSingle() },
            AttributeValueType.KindFloat64 => new AttributeValueUnion
                { ValueType = AttributeValueType.KindFloat64, DoubleValue = reader.GetDouble() },
            AttributeValueType.KindBool    => new AttributeValueUnion
                { ValueType = AttributeValueType.KindBool,    BoolValue   = reader.GetBoolean() },
            AttributeValueType.KindString  => new AttributeValueUnion
                { ValueType = AttributeValueType.KindString,
                  StringValue = InternString(reader.GetString()) },
            _ => default,
        };
    }

    /// <summary>
    /// Returns a pooled reference for <paramref name="value"/>, reducing GC pressure
    /// when the same string is repeatedly received (e.g. <c>"FORCE_OPPOSING"</c>).
    /// </summary>
    private string? InternString(string? value)
        => value is null ? null : _stringPool.GetOrAdd(value, static v => v);

    /// <summary>Returns <c>true</c> if every byte in <paramref name="bytes"/> is an ASCII digit.</summary>
    private static bool IsAllDigits(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;
        foreach (byte b in bytes)
            if (b < (byte)'0' || b > (byte)'9') return false;
        return true;
    }

    /// <summary>Parses a span of ASCII digit bytes to an <see cref="int"/>.</summary>
    private static int ParseInt(ReadOnlySpan<byte> bytes)
    {
        int value = 0;
        foreach (byte b in bytes)
            value = value * 10 + (b - '0');
        return value;
    }
}
