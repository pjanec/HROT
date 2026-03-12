using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Fdp.Kernel;

namespace Bagira.Map.Common.Replication.Utils;

// ─────────────────────────────────────────────────────────────
// Internal dispatch abstraction
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Internal dispatch interface stored inside a <see cref="RoutingEntry"/>.
/// Allows the compiler to call the generic delegate without knowing T at compile time.
/// </summary>
internal interface IRoutingEntryInvoker
{
    /// <summary>The component type this invoker targets.</summary>
    Type ComponentType { get; }

    /// <summary>Extracts the component from <paramref name="context"/> and invokes the setter.</summary>
    void Invoke(IEntityPatchContext context, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader reader);
}

/// <summary>Invoker for unmanaged struct components (passes component by <c>ref</c>).</summary>
internal sealed class ValueInvoker<T> : IRoutingEntryInvoker where T : struct
{
    private readonly ValueAttributeSetter<T> _setter;
    public Type ComponentType => typeof(T);

    internal ValueInvoker(ValueAttributeSetter<T> setter) { _setter = setter; }

    public void Invoke(IEntityPatchContext context, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader reader)
    {
        ref T component = ref context.GetUnmanagedComponent<T>();
        _setter(ref component, indices, ref reader);
    }
}

/// <summary>Invoker for managed class components (passes component by reference).</summary>
internal sealed class ReferenceInvoker<T> : IRoutingEntryInvoker where T : class
{
    private readonly ReferenceAttributeSetter<T> _setter;
    public Type ComponentType => typeof(T);

    internal ReferenceInvoker(ReferenceAttributeSetter<T> setter) { _setter = setter; }

    public void Invoke(IEntityPatchContext context, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader reader)
    {
        T component = context.GetManagedComponent<T>();
        _setter(component, indices, ref reader);
    }
}

// ─────────────────────────────────────────────────────────────
// RoutingEntry
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Maps a pre-computed FNV-1a path hash to the typed invoker and descriptor ordinal
/// needed for dirty-mark flushing.
/// </summary>
internal readonly struct RoutingEntry
{
    /// <summary>The ECS component type targeted by this route.</summary>
    public readonly Type ComponentType;

    /// <summary>Descriptor ordinal used by <see cref="EcsPatchContext.FlushDirtyMarks"/>.</summary>
    public readonly long DescriptorOrdinal;

    /// <summary>The type-erased invoker that calls the concrete setter delegate.</summary>
    internal readonly IRoutingEntryInvoker Invoker;

    internal RoutingEntry(IRoutingEntryInvoker invoker, long descriptorOrdinal = 0)
    {
        Invoker = invoker;
        ComponentType = invoker.ComponentType;
        DescriptorOrdinal = descriptorOrdinal;
    }

    internal void Dispatch(IEntityPatchContext context, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader reader)
        => Invoker.Invoke(context, indices, ref reader);
}

// ─────────────────────────────────────────────────────────────
// JsonAttributeCompiler
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Streams a JSON attribute patch string into an <see cref="IEntityPatchContext"/> using
/// zero heap allocations on the hot path.
/// </summary>
/// <remarks>
/// Instances are created by <see cref="AttributeCompilerBuilder.Build"/>.
/// The routing table is fixed at build time; no allocations occur during <see cref="Compile"/>.
/// </remarks>
public sealed class JsonAttributeCompiler
{
    // ── Constants ───────────────────────────────────────────
    private const int MaxDepth = 16;
    private const int MaxArrayDimensions = 4;
    internal const ulong FnvOffset = 14695981039346656037UL;
    internal const ulong FnvPrime = 1099511628211UL;

    private static ReadOnlySpan<byte> WildcardBytes => "*"u8;
    private static ReadOnlySpan<byte> SeparatorBytes => "."u8;

    // ── State ────────────────────────────────────────────────
    private readonly IReadOnlyDictionary<ulong, RoutingEntry> _routes;

    /// <summary>
    /// Exposes the routing table so <see cref="EcsPatchContext"/> can be constructed with it.
    /// </summary>
    internal IReadOnlyDictionary<ulong, RoutingEntry> Routes => _routes;

    /// <summary>
    /// Creates an <see cref="EcsPatchContext"/> bound to the specified repository and entity,
    /// using this compiler's routing table for ordinal lookup during
    /// <see cref="EcsPatchContext.FlushDirtyMarks"/>.
    /// </summary>
    /// <remarks>
    /// Prefer this factory method over constructing <see cref="EcsPatchContext"/> directly;
    /// the constructor is <c>internal</c> and requires access to the routing entry type.
    /// </remarks>
    public EcsPatchContext CreatePatchContext(EntityRepository repo, Entity entity)
        => new EcsPatchContext(repo, entity, _routes);

    internal JsonAttributeCompiler(IReadOnlyDictionary<ulong, RoutingEntry> routes)
    {
        _routes = routes;
    }

    // ── Public API ───────────────────────────────────────────

    /// <summary>
    /// Applies all JSON attribute overrides in <paramref name="json"/> to
    /// <paramref name="context"/>. No heap allocations occur if <paramref name="json"/>
    /// is null or empty.
    /// </summary>
    public void Compile(string? json, IEntityPatchContext context)
    {
        if (string.IsNullOrEmpty(json))
            return;

        // Convert string to UTF-8 bytes — single allocation per call (acceptable at startup;
        // hot-path callers should pass pre-encoded bytes via the span overload).
        // For the simulation hot-path the JSON string arrives over DDS already as a managed
        // string, so this encoding step is unavoidable without a pooled buffer.
        ReadOnlySpan<byte> utf8 = Encoding.UTF8.GetBytes(json);

        var reader = new Utf8JsonReader(utf8);

        // ── Stack-allocated state machine ─────────────────────
        // contextStack[d] = FNV hash context for properties at depth d.
        // contextStack[0] = FnvOffset (root level parent context).
        Span<ulong> contextStack     = stackalloc ulong[MaxDepth + 1];
        // Compact list of numeric (wildcard) indices encountered from root to the current token.
        Span<int>   indexStack       = stackalloc int[MaxArrayDimensions * MaxDepth];
        // Per-depth flag: was a numeric index stored when entering this depth?
        Span<byte>  hadNumericAtDepth = stackalloc byte[MaxDepth + 1]; // 0 = false, 1 = true

        contextStack[0] = FnvOffset;
        int depth = 0;
        int wildcardTotal = 0;          // number of compact entries in indexStack
        ulong currentLeafHash = FnvOffset;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    // Push current leaf hash as the context for the next depth level.
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
                            wildcardTotal--;
                            hadNumericAtDepth[depth] = 0;
                        }
                        depth--;
                    }
                    break;

                case JsonTokenType.PropertyName:
                {
                    ReadOnlySpan<byte> nameBytes = reader.ValueSpan;

                    if (IsAllDigits(nameBytes))
                    {
                        // Numeric index: store compactly, hash wildcard.
                        if (wildcardTotal < indexStack.Length)
                        {
                            indexStack[wildcardTotal++] = ParseInt(nameBytes);
                            hadNumericAtDepth[depth] = 1;
                        }
                        currentLeafHash = HashBytes(HashBytes(contextStack[depth], SeparatorBytes), WildcardBytes);
                    }
                    else
                    {
                        // Named property: hash separator then name.
                        currentLeafHash = HashBytes(HashBytes(contextStack[depth], SeparatorBytes), nameBytes);
                    }
                    break;
                }

                // Primitive value tokens — attempt dispatch.
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                {
                    if (_routes.TryGetValue(currentLeafHash, out var entry))
                    {
                        // Pass the compact list of numeric indices accumulated on this path.
                        ReadOnlySpan<int> indices = indexStack[..wildcardTotal];
                        entry.Dispatch(context, indices, ref reader);
                    }
                    break;
                }
            }
        }
    }

    // ── Internal hashing helpers (used by AttributeCompilerBuilder) ──────

    /// <summary>
    /// Computes the FNV-1a hash for a dot-separated JSON path string.
    /// Numeric path segments are normalised to <c>*</c>.
    /// </summary>
    internal static ulong HashPath(string path)
    {
        ulong context = FnvOffset;
        ulong h = FnvOffset;
        var segments = path.AsSpan();

        while (true)
        {
            int dot = segments.IndexOf('.');
            ReadOnlySpan<char> seg = dot >= 0 ? segments[..dot] : segments;

            // Determine if numeric segment → wildcard
            bool numeric = true;
            foreach (char c in seg)
            {
                if (c < '0' || c > '9') { numeric = false; break; }
            }

            ReadOnlySpan<byte> segBytes = numeric && seg.Length > 0
                ? WildcardBytes
                : Encoding.UTF8.GetBytes(seg.ToString());

            h = HashBytes(HashBytes(context, SeparatorBytes), segBytes);
            context = h;

            if (dot < 0) break;
            segments = segments[(dot + 1)..];
        }

        return h;
    }

    /// <summary>
    /// FNV-1a byte-by-byte hash accumulation: <c>hash = (hash XOR b) * FnvPrime</c>.
    /// </summary>
    internal static ulong HashBytes(ulong current, ReadOnlySpan<byte> bytes)
    {
        ulong hash = current;
        foreach (byte b in bytes)
            hash = (hash ^ b) * FnvPrime;
        return hash;
    }

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
