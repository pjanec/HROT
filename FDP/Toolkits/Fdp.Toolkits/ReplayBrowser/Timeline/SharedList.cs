using System;
using System.Collections;
using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1361</c> ② — a read-only VIEW of the first <see cref="Count"/> elements of a shared array. The live index appends to a grow-only
    /// array and every published snapshot holds (that array, its count at the time): slots below a snapshot's count are never written again, and
    /// a growth copies into a new array that older snapshots never see — so a snapshot is immutable, and publishing one is O(1), not a copy of
    /// every event ever indexed. A reader only ever touches indices below its own count.
    /// </summary>
    public sealed class SharedList<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;

        public SharedList(T[] items, int count)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            if ((uint)count > (uint)items.Length) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }

        /// <summary>A view of the whole array.</summary>
        public SharedList(T[] items) : this(items, items?.Length ?? 0) { }

        public static readonly SharedList<T> Empty = new(Array.Empty<T>(), 0);

        public int Count { get; }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _items[index];
            }
        }

        /// <summary>The viewed elements, without a copy.</summary>
        public ReadOnlyMemory<T> Memory => new(_items, 0, Count);
        public ReadOnlySpan<T> Span => new(_items, 0, Count);

        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return _items[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The writer's side of a <see cref="SharedList{T}"/>: append-only, capacity doubling (a growth copies; snapshots keep the old array).</summary>
    internal sealed class GrowOnlyBuffer<T>
    {
        private T[] _items = new T[16];
        public int Count { get; private set; }

        public void Add(T item)
        {
            if (Count == _items.Length) Array.Resize(ref _items, _items.Length * 2);   // a new array: a published snapshot keeps the old one
            _items[Count++] = item;
        }

        /// <summary>Overwrites a slot that is not yet published (the caller's guarantee) or appends when <paramref name="index"/> is the end.</summary>
        public T this[int index] { get => _items[index]; set => _items[index] = value; }

        public SharedList<T> Snapshot() => new(_items, Count);
    }
}
