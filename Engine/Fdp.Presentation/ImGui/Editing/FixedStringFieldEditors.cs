using System;
using Fdp.Core;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

public sealed class FixedString32FieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(FixedString32);

    public EditNode CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        return new EditNode(
            id,
            name,
            jsonPath,
            EditNodeKind.String,
            typeof(string),
            new FixedString32BindingAdapter(binding),
            null,
            metadata);
    }

    private sealed class FixedString32BindingAdapter : IValueBinding
    {
        private readonly IValueBinding _inner;

        public FixedString32BindingAdapter(IValueBinding inner)
        {
            _inner = inner;
        }

        public Type ValueType => typeof(string);

        public object? GetBoxed()
        {
            var raw = _inner.GetBoxed();
            return raw?.ToString();
        }

        public void SetBoxed(object? value)
        {
            var str = value as string ?? string.Empty;
            _inner.SetBoxed(new FixedString32(str));
        }

        public bool TryGetSpan(out Span<byte> bytes)
        {
            return _inner.TryGetSpan(out bytes);
        }
    }
}

public sealed class FixedString64FieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(FixedString64);

    public EditNode CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        return new EditNode(
            id,
            name,
            jsonPath,
            EditNodeKind.String,
            typeof(string),
            new FixedString64BindingAdapter(binding),
            null,
            metadata);
    }

    private sealed class FixedString64BindingAdapter : IValueBinding
    {
        private readonly IValueBinding _inner;

        public FixedString64BindingAdapter(IValueBinding inner)
        {
            _inner = inner;
        }

        public Type ValueType => typeof(string);

        public object? GetBoxed()
        {
            var raw = _inner.GetBoxed();
            return raw?.ToString();
        }

        public void SetBoxed(object? value)
        {
            var str = value as string ?? string.Empty;
            _inner.SetBoxed(new FixedString64(str));
        }

        public bool TryGetSpan(out Span<byte> bytes)
        {
            return _inner.TryGetSpan(out bytes);
        }
    }
}
