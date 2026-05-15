using System.Collections.Generic;
using System.Text.Json;

namespace Fdp.Toolkit.ReplayBrowser.Diff
{
    public abstract class DiffNode
    {
        public string Name { get; }
        public bool IsModified { get; protected set; }

        protected DiffNode(string name) => Name = name;
    }

    public sealed class DiffObject : DiffNode
    {
        public List<DiffNode> Children { get; } = new();

        public DiffObject(string name) : base(name) { }

        public void EvaluateModificationState()
        {
            // An object is considered modified if any of its descendants are modified
            IsModified = Children.Exists(c => c.IsModified);
        }
    }

    public sealed class DiffValue : DiffNode
    {
        public string OldValue { get; }
        public string NewValue { get; }
        public JsonValueKind ValueType { get; }

        public DiffValue(string name, string oldValue, string newValue, JsonValueKind valueType, bool isModified)
            : base(name)
        {
            OldValue = oldValue;
            NewValue = newValue;
            ValueType = valueType;
            IsModified = isModified;
        }
    }
}
