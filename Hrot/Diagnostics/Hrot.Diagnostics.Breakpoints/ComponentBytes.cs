using System;
using System.Runtime.InteropServices;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// ⭐⭐ <b>The managed byte image of a boxed unmanaged value — ONE implementation.</b>
///
/// <para>⚠ <b><c>Marshal.StructureToPtr</c> is deliberately not used:</b> it writes the MARSHALLED
/// layout, which differs from the managed one on <c>bool</c> (1 byte vs 4). ⛔ The ECS stores the
/// managed layout, so a marshalled image would name the wrong offsets — silently, and only for some
/// types.</para>
///
/// <para>⭐ <b>Extracted in Batch 84</b> because the editor's live write needs the same image the
/// staging diff needs. 📌 Ruling 9: ⛔ two copies of this would be two answers to <i>"what are this
/// value's bytes?"</i>, and the one that is wrong is wrong only for <c>bool</c> — the hardest kind of
/// disagreement to notice.</para>
/// </summary>
public static class ComponentBytes
{
    /// <summary>⭐ <paramref name="sizeBytes"/> bytes of <paramref name="boxed"/>'s managed image.</summary>
    public static unsafe byte[] Of(object boxed, int sizeBytes)
    {
        if (boxed is null) throw new ArgumentNullException(nameof(boxed));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        var bytes  = new byte[sizeBytes];
        if (sizeBytes == 0) return bytes;

        var handle = GCHandle.Alloc(boxed, GCHandleType.Pinned);
        try
        {
            fixed (byte* dest = bytes)
                System.Buffer.MemoryCopy((void*)handle.AddrOfPinnedObject(), dest, sizeBytes, sizeBytes);
        }
        finally { handle.Free(); }
        return bytes;
    }

    /// <summary>
    /// ⭐ The ECS chunk stride for <paramref name="type"/> — <c>ComponentType&lt;T&gt;.Size</c>, i.e.
    /// <c>Unsafe.SizeOf&lt;T&gt;()</c>. ⛔ Not <c>Marshal.SizeOf</c>, for the reason above.
    /// </summary>
    public static int SizeOf(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        lock (_sizeCache)
        {
            if (_sizeCache.TryGetValue(type, out int cached)) return cached;
            var generic = typeof(Fdp.Core.ComponentType<>).MakeGenericType(type);
            var prop    = generic.GetProperty("Size",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            int size    = (int)prop.GetValue(null)!;
            _sizeCache[type] = size;
            return size;
        }
    }

    private static readonly System.Collections.Generic.Dictionary<Type, int> _sizeCache = new();
}
