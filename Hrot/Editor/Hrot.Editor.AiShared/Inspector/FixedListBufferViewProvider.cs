using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using StructEdit.Core;
using StructEdit.Core.UnionSupport;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// FC-3c (Q#21-D3/D-p P1) — StructEdit <see cref="IBufferViewProvider"/> that claims the
/// <c>[InlineArray]</c> buffer of a canonical fixed-list WRAPPER
/// (<see cref="FixedListShape"/>: <c>int Count</c> + one buffer field) and presents a
/// COUNT-BOUNDED element window instead of the raw all-capacity expansion:
/// <list type="bullet">
///   <item>the window is <c>min(max(Count,0), N)</c> — a stale/garbage Count can never
///   surface out-of-window slots (F2);</item>
///   <item>the collapsed row's name is the SHARED summary string
///   (<see cref="FixedListFormatter"/> — the same one the Blueprints debugger watch renders),
///   so every inspector shows one identical form;</item>
///   <item>display-only v1 (Q#21-D-e): element bindings exist (StructEdit writes through
///   them natively), but no add/remove/resize surface is offered here.</item>
/// </list>
/// Register on any StructEdit host via
/// <c>ComponentEditServiceBuilder.RegisterBufferViewProvider(new FixedListBufferViewProvider())</c>.
/// This provider is HOST code by design — StructEdit (an independent ExtDeps library) gains
/// only the generic InlineArray provider hook, never the HROT shape convention.
/// </summary>
public sealed class FixedListBufferViewProvider : IBufferViewProvider
{
    public bool CanCreateView(BufferViewRequest request)
        => TryResolveWrapper(request, out _, out _, out _, out _, out _);

    public BufferViewResult CreateView(BufferViewRequest request)
    {
        if (!TryResolveWrapper(request, out var wrapperType, out var elemType,
                out int capacity, out var countPath, out var bufferField))
            throw new InvalidOperationException("CreateView called without a matching wrapper.");

        int count = request.ReadSibling<int>(countPath);
        string title = BuildSummaryTitle(request, wrapperType, elemType, capacity, count, bufferField);
        return request.ProjectBufferAsElements(elemType, count, title, capacity);
    }

    /// <summary>
    /// The buffer node qualifies when its PARENT struct (resolved by walking the component
    /// type along the buffer path minus the last segment) matches the fixed-list wrapper
    /// shape AND the path's last segment is that shape's buffer field.
    /// </summary>
    private static bool TryResolveWrapper(
        BufferViewRequest request,
        out Type wrapperType, out Type elemType, out int capacity,
        out EditPath countPath, out FieldInfo bufferField)
    {
        wrapperType = typeof(void);
        elemType    = typeof(void);
        capacity    = 0;
        countPath   = EditPath.Parse("$");
        bufferField = null!;

        var path = request.BufferPath.Value;
        if (path.StartsWith("$.", StringComparison.Ordinal)) path = path[2..];
        else if (path.StartsWith('$')) path = path[1..];
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;

        // Walk to the buffer field's PARENT type.
        Type parent = request.ComponentType;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var fi = parent.GetField(segments[i], BindingFlags.Public | BindingFlags.Instance);
            if (fi is null) return false;
            parent = fi.FieldType;
        }

        if (!FixedListShape.TryGet(parent, out elemType, out _, out capacity,
                out var countField, out bufferField))
            return false;
        if (bufferField.Name != segments[^1]) return false;

        var parentPath = segments.Length == 1
            ? "$"
            : "$." + string.Join('.', segments[..^1]);
        countPath = EditPath.Parse(parentPath == "$" ? $"$.{countField.Name}" : $"{parentPath}.{countField.Name}");
        wrapperType = parent;
        return true;
    }

    /// <summary>
    /// Renders the collapsed-row title through the SHARED formatter by reassembling the
    /// wrapper's byte image from the public surface (Count sibling + the buffer span).
    /// Falls back to a plain "List&lt;T&gt;[N]" header when the bytes are unavailable.
    /// </summary>
    private static string BuildSummaryTitle(
        BufferViewRequest request, Type wrapperType, Type elemType,
        int capacity, int count, FieldInfo bufferField)
    {
        try
        {
            if (request.BufferBinding.TryGetSpan(out var bufferBytes))
            {
                var image = new byte[Marshal.SizeOf(wrapperType)];
                int countOffset = (int)Marshal.OffsetOf(wrapperType, "Count");
                int itemsOffset = (int)Marshal.OffsetOf(wrapperType, bufferField.Name);
                BitConverter.GetBytes(count).CopyTo(image, countOffset);
                bufferBytes.Slice(0, Math.Min(bufferBytes.Length, image.Length - itemsOffset))
                    .CopyTo(image.AsSpan(itemsOffset));
                if (FixedListFormatter.TryFormat(image, wrapperType, out var summary))
                    return summary;
            }
        }
        catch (ArgumentException) { /* non-blittable wrapper — fall through */ }
        return $"List<{elemType.Name}>[{capacity}]";
    }
}
