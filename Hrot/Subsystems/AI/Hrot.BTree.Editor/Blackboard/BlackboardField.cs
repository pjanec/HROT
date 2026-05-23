using System;

namespace Hrot.BTree.Editor.Blackboard;

public sealed record BlackboardField(
    string Name,
    Type FieldType,
    BlackboardFieldKind Kind);
