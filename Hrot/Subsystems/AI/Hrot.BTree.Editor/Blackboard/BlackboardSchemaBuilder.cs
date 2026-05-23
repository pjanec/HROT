using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Hrot.BTree.Editor.Blackboard;

// Reflects a blackboard struct type to produce a BlackboardSchema.
// Only public instance fields are included (not properties).
public static class BlackboardSchemaBuilder
{
    public static BlackboardSchema Build(Type structType)
    {
        var fields = new List<BlackboardField>();
        foreach (var fi in structType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var kind   = ClassifyFieldType(fi.FieldType);
            int offset = TryGetOffset(structType, fi.Name);
            fields.Add(new BlackboardField(fi.Name, fi.FieldType, kind, offset));
        }
        return new BlackboardSchema(structType, fields.AsReadOnly());
    }

    private static int TryGetOffset(Type structType, string fieldName)
    {
        try
        {
            return (int)Marshal.OffsetOf(structType, fieldName);
        }
        catch
        {
            return -1;
        }
    }

    private static BlackboardFieldKind ClassifyFieldType(Type t)
    {
        if (t == typeof(bool))   return BlackboardFieldKind.Bool;

        if (t == typeof(int)    || t == typeof(float)  || t == typeof(double) ||
            t == typeof(long)   || t == typeof(short)  || t == typeof(byte)   ||
            t == typeof(uint)   || t == typeof(ulong)  || t == typeof(ushort) ||
            t == typeof(sbyte)  || t == typeof(decimal))
            return BlackboardFieldKind.Numeric;

        if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4))
            return BlackboardFieldKind.Vector;

        if (t.IsEnum)   return BlackboardFieldKind.Enum;
        if (t.IsValueType && !t.IsPrimitive) return BlackboardFieldKind.Struct;
        return BlackboardFieldKind.Other;
    }
}
