using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Fdp.Core.FlightRecorder;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94d</c>) — turns a MANAGED watched value into BYTES so the change monitor can
/// compare it.</b>
///
/// <para>📄 <b>The ruling, verbatim</b> *(<c>R-103</c> / <c>Q46</c> §2 rule 9, the user)*: <i>"we have
/// fast pre-compiled binary serializer mechanism for any component and i guess it can be used for any
/// class. it produces bytes. we compare these bytes. <b>No way comparing rendered text!</b>"</i></para>
///
/// <para>⭐ <b>Why a bridge at all.</b> <c>FdpAutoSerializer.Serialize&lt;T&gt;</c> is generic and a
/// watch row holds an <c>object</c>. ⭐⭐ The shape is copied from
/// <c>FdpPolymorphicSerializer.CompileWriteDelegate</c>, which builds
/// <c>(writer, obj) =&gt; FdpAutoSerializer.Serialize&lt;T&gt;((T)obj, writer)</c> by
/// <c>MakeGenericMethod</c> and caches it per <c>Type</c> — ⛔ <b>without</b> that class's
/// <c>[FdpPolymorphicType]</c> registry, which we cannot require of arbitrary watched types.</para>
///
/// <para>⛔⛔ <b>THE FENCE, and it is the reason this class exists rather than a two-line call.</b>
/// 📐 Measured on <c>FdpAutoSerializer</c> *(<c>Q46</c> §3)*: it has <b>no cycle guard</b> — a
/// back-reference recurses until the stack dies — and get-only properties are skipped. ⇒
/// ⭐⭐ <b>the first time a TYPE throws or blows the size cap, that type is recorded as
/// not-comparable and is never serialized again.</b> ⭐ Such a row simply <b>never highlights</b>;
/// ⛔ it must never crash the editor, and ⛔ it must never fall back to comparing text.</para>
///
/// <para>⛔⛔ <b>A <c>StackOverflowException</c> CANNOT BE CAUGHT in .NET</b> — it kills the process.
/// ⇒ ⭐⭐⭐ <b>the cycle tooth is fenced BEFORE the serializer runs, by inspecting the TYPE</b>, not by
/// catching anything.</para>
///
/// <para>⚠⚠ <b>MEASURED, Batch 94 — and it is why this class does what it does.</b> The first design
/// here was a size cap checked <em>during</em> the write, on the reasoning that <i>"a cycle emits bytes
/// without bound and trips the cap long before the stack goes."</i> 🔴 <b>FALSE, and the rail proved
/// it by aborting the whole test host:</b> a self-referencing node with a single reference member
/// recurses <b>without writing a single byte per level</b>, so the stack dies first and no cap can
/// ever be consulted. ⇒ ⭐ <b>a dynamic fence cannot work for tooth ③; only a static one can.</b></para>
///
/// <para>⭐ <see cref="CanReachItself"/> walks the type graph once per type — the same members
/// <c>FdpAutoSerializer</c> would follow — and fences any type that can reach itself.
/// ⚠ <b>Conservative on purpose:</b> a tree-shaped type is fenced even when a particular instance is
/// acyclic, because the fence must be a property of the TYPE *(the serializer is compiled per type,
/// and an instance check would have to run the dangerous code to find out)*. ⭐ Such a row never
/// highlights; ⛔ it never crashes.</para>
///
/// <para>⭐ The size cap SURVIVES as a second, independent fence — for a genuinely huge but acyclic
/// value *(a large collection)*, where it does trip correctly.</para>
///
/// <para>⭐ <b>One pooled buffer per instance</b> — ⛔ not one stream per row per tick, which the
/// allocation-trait rails would see. ⚠ Therefore <b>not thread-safe</b>, and it does not need to be:
/// each panel's sampler owns one and samples on its own UI thread.</para>
/// </summary>
public sealed class ManagedValueBytes
{
    /// <summary>
    /// ⭐ The size cap. Generous for a real watched value, and small enough that a cyclic graph trips
    /// it in microseconds. ⚠ Tripping it is not an error — it marks the TYPE not-comparable.
    /// </summary>
    public const int MaxBytes = 64 * 1024;

    private static readonly ConcurrentDictionary<Type, Action<BinaryWriter, object>> Writers = new();

    /// <summary>
    /// ⭐⭐ Types that threw or overflowed once. ⛔ <b>Per type, never per row</b> — the point of the
    /// fence is that a bad type costs one failed attempt in the whole session, not one per frame.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, byte> NotComparable = new();

    private readonly MemoryStream _buffer = new(capacity: 256);

    /// <summary>⭐ Whether <paramref name="type"/> has been fenced off. ⛔ Diagnostics and rails.</summary>
    public static bool IsNotComparable(Type type) => NotComparable.ContainsKey(type);

    /// <summary>⛔ Rails only — the fence is process-wide and would otherwise leak between tests.</summary>
    internal static void ResetFenceForTests() => NotComparable.Clear();

    /// <summary>
    /// Serialises <paramref name="value"/> and returns its bytes, or <c>null</c> when it cannot be
    /// compared.
    /// </summary>
    /// <remarks>
    /// ⭐ <c>null</c> means <i>"this row never highlights"</i> — ⛔ it does <b>not</b> mean "unchanged",
    /// and the caller must not treat it as an empty byte array *(which would compare equal to another
    /// unserialisable value and claim nothing changed)*.
    /// </remarks>
    public byte[]? TryGetBytes(object? value)
    {
        if (value is null) return Array.Empty<byte>();

        var type = value.GetType();
        if (NotComparable.ContainsKey(type)) return null;

        // ⭐⭐⭐ THE CYCLE FENCE, applied BEFORE the serializer is ever invoked. ⛔ A cyclic type is
        //    refused outright: catching the overflow it would cause is not possible in .NET.
        if (CycleRisk.GetOrAdd(type, CanReachItself))
        {
            NotComparable.TryAdd(type, 0);
            return null;
        }

        try
        {
            _buffer.SetLength(0);
            using var capped = new CappedStream(_buffer, MaxBytes);
            using (var writer = new BinaryWriter(capped, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                Writers.GetOrAdd(type, CompileWriter)(writer, value);
                writer.Flush();
            }
            return _buffer.ToArray();
        }
        catch (Exception)
        {
            // ⭐⭐ The fence. ⛔ Deliberately catches EVERYTHING: a serializer that throws
            //   TargetInvocationException, a cap overflow, a type with no accessible members — the
            //   editor's answer to all of them is the same, and it is "this row never highlights".
            NotComparable.TryAdd(type, 0);
            return null;
        }
    }

    /// <summary>⭐ Cached answer to "can this type reach itself?". ⛔ Computed once per type.</summary>
    private static readonly ConcurrentDictionary<Type, bool> CycleRisk = new();

    /// <summary>
    /// ⭐⭐ Walks the type graph the serializer would follow and reports whether
    /// <paramref name="root"/> is reachable from itself.
    /// </summary>
    /// <remarks>
    /// ⭐ Member selection MIRRORS <c>FdpAutoSerializer</c>'s — public instance fields plus read/write
    /// properties — and follows generic arguments so a <c>List&lt;Node&gt;</c> counts as reaching
    /// <c>Node</c>. ⚠ Where the two could disagree this errs toward following MORE, because a false
    /// "cyclic" costs one row's highlight while a false "safe" costs the process.
    /// </remarks>
    private static bool CanReachItself(Type root)
    {
        var seen  = new HashSet<Type> { root };
        var stack = new Stack<Type>();
        foreach (var next in Referenced(root)) stack.Push(next);

        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t == root) return true;
            if (!seen.Add(t)) continue;
            foreach (var next in Referenced(t)) stack.Push(next);
        }
        return false;
    }

    private static IEnumerable<Type> Referenced(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            yield break;

        if (type.IsArray)
        {
            var element = type.GetElementType();
            if (element != null) yield return element;
            yield break;
        }

        if (type.IsGenericType)
            foreach (var arg in type.GetGenericArguments())
                yield return arg;

        const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;

        foreach (var f in type.GetFields(Instance))
            yield return f.FieldType;

        foreach (var p in type.GetProperties(Instance))
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                yield return p.PropertyType;
    }

    private static Action<BinaryWriter, object> CompileWriter(Type type)
    {
        var writerParam = Expression.Parameter(typeof(BinaryWriter), "writer");
        var objParam    = Expression.Parameter(typeof(object), "obj");

        var serialize = typeof(FdpAutoSerializer)
            .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(type);

        var call = Expression.Call(serialize, Expression.Convert(objParam, type), writerParam);
        return Expression.Lambda<Action<BinaryWriter, object>>(call, writerParam, objParam).Compile();
    }

    /// <summary>
    /// ⭐ A write-through stream that throws once <paramref name="limit"/> bytes have been written.
    /// ⛔ The cap must be enforced DURING the write — a cyclic graph never returns, so measuring the
    /// finished buffer would never happen.
    /// </summary>
    private sealed class CappedStream : Stream
    {
        private readonly Stream _inner;
        private readonly int    _limit;
        private long            _written;

        public CappedStream(Stream inner, int limit) { _inner = inner; _limit = limit; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written += count;
            if (_written > _limit)
                throw new InvalidOperationException(
                    $"watched value exceeded {_limit} bytes — treated as not-comparable");
            _inner.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            if (++_written > _limit)
                throw new InvalidOperationException(
                    $"watched value exceeded {_limit} bytes — treated as not-comparable");
            _inner.WriteByte(value);
        }

        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
