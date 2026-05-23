using System;
using System.Collections.Generic;

namespace Hrot.BTree.Editor.Blackboard;

public sealed class BlackboardSchema
{
    public Type StructType { get; }
    public IReadOnlyList<BlackboardField> Fields { get; }

    public BlackboardSchema(Type structType, IReadOnlyList<BlackboardField> fields)
    {
        StructType = structType;
        Fields = fields;
    }
}
