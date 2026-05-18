using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Zero-allocation edge compiler that converts a UTF-8 JSON attribute patch into
/// a sequence of typed values emitted to a caller-supplied
/// <see cref="IAttributeRecordEmitter"/>.
///
/// <para>
/// Handles three JSON shapes without heap allocation:
/// <list type="bullet">
///   <item>Flat dotted keys: <c>{ "GeoPosition.Latitude": 32.0 }</c></item>
///   <item>Nested objects: <c>{ "GeoPosition": { "Latitude": 32.0 } }</c></item>
///   <item>Array-indexed nested objects: <c>{ "Weapon": { "2": { "Ammo": 5 } } }</c>
///         — integer string keys are captured as SubIndex1 / SubIndex2.</item>
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
    /// Per-instance string intern pool for <see cref="AttributeValueKind.CsString"/> values.
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
    /// Compiles all JSON attribute values in <paramref name="utf8Json"/> and emits
    /// typed attribute records to <paramref name="emitter"/>.
    /// </summary>
    /// <param name="utf8Json">Valid UTF-8 JSON object. Empty input is a no-op.</param>
    /// <param name="emitter">Callback that receives each resolved attribute value.</param>
    public void Compile(ReadOnlySpan<byte> utf8Json, IAttributeRecordEmitter emitter)
    {
        if (utf8Json.IsEmpty)
            return;

        var reader = new Utf8JsonReader(utf8Json);

        // ── Stack-allocated state machine ─────────────────────────────────────
        // contextStack[d] = accumulated FNV-1a hash context at depth d.
        // contextStack[0] = FnvOffset (root level parent hash).
        Span<ulong> contextStack      = stackalloc ulong[MaxDepth + 1];
        // Per-depth flag: was a numeric index key consumed at this depth.
        Span<byte>  hadNumericAtDepth = stackalloc byte[MaxDepth + 1];

        contextStack[0] = JsonAttributeCompiler.FnvOffset;
        int    depth           = 0;
        ulong  currentLeafHash = JsonAttributeCompiler.FnvOffset;

        // Sub-index accumulation: numeric key strings become SubIndex1/SubIndex2.
        short  subIndex1       = 0;
        short  subIndex2       = 0;
        int    numericCount    = 0; // how many numeric keys are active in current path

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
                    if (_routes.TryGetValue(currentLeafHash, out var entry))
                    {
                        EmitRecord(ref reader, entry, subIndex1, subIndex2, emitter);
                    }
                    break;
                }
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches to the correct <see cref="IAttributeRecordEmitter"/> overload based on
    /// <see cref="EdgeSchemaEntry.ExpectedKind"/> and the current JSON token value.
    /// </summary>
    private void EmitRecord(
        ref Utf8JsonReader reader,
        EdgeSchemaEntry entry,
        short subIndex1,
        short subIndex2,
        IAttributeRecordEmitter emitter)
    {
        switch (entry.ExpectedKind)
        {
            case AttributeValueKind.CsInt32:
                emitter.EmitInt32(entry.AttributeId, reader.GetInt32(), subIndex1, subIndex2);
                break;
            case AttributeValueKind.CsInt64:
                emitter.EmitInt64(entry.AttributeId, reader.GetInt64(), subIndex1, subIndex2);
                break;
            case AttributeValueKind.CsFloat32:
                emitter.EmitFloat32(entry.AttributeId, reader.GetSingle(), subIndex1, subIndex2);
                break;
            case AttributeValueKind.CsFloat64:
                emitter.EmitFloat64(entry.AttributeId, reader.GetDouble(), subIndex1, subIndex2);
                break;
            case AttributeValueKind.Bool:
                emitter.EmitBool(entry.AttributeId, reader.GetBoolean(), subIndex1, subIndex2);
                break;
            case AttributeValueKind.CsString:
                emitter.EmitString(entry.AttributeId, InternString(reader.GetString()), subIndex1, subIndex2);
                break;
        }
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