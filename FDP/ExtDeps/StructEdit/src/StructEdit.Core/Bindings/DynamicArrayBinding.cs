using System.Collections;

namespace StructEdit.Core.Bindings;

/// <summary>
/// Binding for a <c>List&lt;T&gt;</c> or <c>T[]</c> field/property on a managed object.
/// Supports <see cref="Resize"/> with atomic parent writeback.
/// </summary>
internal sealed class DynamicArrayBinding : IContainerOps
{
    private object _container;
    private readonly IValueBinding _parentBinding;
    private readonly bool _isList;

    public Type ElementType { get; }
    public Type ValueType => _container.GetType();
    public int Count { get; private set; }
    public bool CanResize => true;

    public DynamicArrayBinding(object container, IValueBinding parentBinding, Type elementType)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _parentBinding = parentBinding ?? throw new ArgumentNullException(nameof(parentBinding));
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        _isList = container is IList && container.GetType().IsGenericType;
        Count = ((IList)container).Count;
    }

    public object? GetBoxed() => _container;

    public void SetBoxed(object? value)
    {
        _container = value ?? throw new ArgumentNullException(nameof(value));
        Count = ((IList)_container).Count;
    }

    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }

    public IValueBinding GetElementBinding(int index) => new ArrayElementBinding(_container, index, ElementType);

    public void Resize(int newCount)
    {
        int copyCount = Math.Min(Count, newCount);
        var oldList = (IList)_container;
        object newContainer;

        if (!_isList)
        {
            // T[] — create a new array and copy elements
            var newArray = Array.CreateInstance(ElementType, newCount);
            for (int i = 0; i < copyCount; i++)
                newArray.SetValue(oldList[i], i);
            newContainer = newArray;
        }
        else
        {
            // List<T> — create new List<T> and populate
            var listType = typeof(List<>).MakeGenericType(ElementType);
            var newList = (IList)Activator.CreateInstance(listType)!;
            for (int i = 0; i < copyCount; i++)
                newList.Add(oldList[i]);
            for (int i = copyCount; i < newCount; i++)
                newList.Add(ElementType.IsValueType ? Activator.CreateInstance(ElementType) : null);
            newContainer = newList;
        }

        // Write back to parent BEFORE updating internal state (per binding contract)
        _parentBinding.SetBoxed(newContainer);
        _container = newContainer;
        Count = newCount;
    }

    // ── S4 list operations (IContainerOps) — each count change builds a NEW container and writes it back, like Resize ──

    public bool CanInsertRemove => true;

    public object? NewElement() => ContainerOps.DefaultOf(ElementType);

    public void Insert(int index, object? value)
    {
        if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        Rebuild(Count + 1, (old, add) => { for (int i = 0; i < index; i++) add(old[i]); add(value); for (int i = index; i < old.Count; i++) add(old[i]); });
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        Rebuild(Count - 1, (old, add) => { for (int i = 0; i < old.Count; i++) if (i != index) add(old[i]); });
    }

    public void Move(int from, int to) => ContainerOps.Move(this, from, to);

    public void Duplicate(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        Insert(index + 1, ContainerOps.Clone(((IList)_container)[index], ElementType));
    }

    public void Clear() => Rebuild(0, (_, _) => { });

    private void Rebuild(int newCount, Action<IList, Action<object?>> fill)
    {
        var old = (IList)_container;
        object next;
        if (!_isList)
        {
            var arr = Array.CreateInstance(ElementType, newCount);
            int k = 0;
            fill(old, v => arr.SetValue(v, k++));
            next = arr;
        }
        else
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ElementType))!;
            fill(old, v => list.Add(v));
            next = list;
        }
        _parentBinding.SetBoxed(next);   // write back BEFORE updating internal state (the binding contract, as Resize)
        _container = next;
        Count = newCount;
    }

    // ── element binding ──────────────────────────────────────────────────────────

    private sealed class ArrayElementBinding : IValueBinding
    {
        private readonly object _container;
        private readonly int _index;

        public Type ValueType { get; }

        public ArrayElementBinding(object container, int index, Type valueType)
        {
            _container = container;
            _index = index;
            ValueType = valueType;
        }

        public object? GetBoxed()
        {
            if (_container is Array arr) return arr.GetValue(_index);
            if (_container is IList list) return list[_index];
            throw new NotSupportedException($"Unsupported container type: {_container.GetType()}");
        }

        public void SetBoxed(object? value)
        {
            if (_container is Array arr) { arr.SetValue(value, _index); return; }
            if (_container is IList list) { list[_index] = value; return; }
            throw new NotSupportedException($"Unsupported container type: {_container.GetType()}");
        }

        public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
    }
}
