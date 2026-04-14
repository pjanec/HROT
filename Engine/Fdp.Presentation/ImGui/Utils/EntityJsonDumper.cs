using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;

namespace FDP.Toolkit.ImGui.Utils;

public static class EntityJsonDumper
{
    public static string Dump(IInspectableSession session, Entity entity)
    {
        var dict = new Dictionary<string, object>();
        dict["EntityId"] = new int[] { entity.Index, entity.Generation };

        var componentsDict = new Dictionary<string, object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        var allTypes = session.GetAllComponentTypes().OrderBy(t => t.Name).ToList();
        foreach (var type in allTypes)
        {
            if (!session.HasComponent(entity, type)) continue;

            object? data = session.GetComponent(entity, type);
            if (data == null) continue;

            componentsDict[type.Name] = MapObject(data, type, visited) ?? new object();
        }

        dict["Components"] = componentsDict;

        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(dict, options);
    }

    private static object? MapObject(object? obj, Type type, HashSet<object> visited)
    {
        if (obj == null) return null;
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid))
            return obj;
        if (type.IsEnum)
            return obj.ToString();

        if (!type.IsValueType && !visited.Add(obj))
        {
            return "<<circular reference>>";
        }

        if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(item != null ? MapObject(item, item.GetType(), visited) : null);
            }
            return list;
        }

        var dict = new Dictionary<string, object?>();

        var inlineArrayAttr = type.GetCustomAttribute<System.Runtime.CompilerServices.InlineArrayAttribute>();
        if (inlineArrayAttr != null)
        {
            int length = inlineArrayAttr.Length;
            var list = new List<object?>();
            var elementField = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault();
            if (elementField != null)
            {
                Type elemType = elementField.FieldType;
                int structSize = Marshal.SizeOf(type);
                IntPtr ptr = Marshal.AllocHGlobal(structSize);
                try
                {
                    Marshal.StructureToPtr(obj, ptr, false);
                    int elemSize = GetSizeOf(elemType);
                    for (int i = 0; i < length; i++)
                    {
                        IntPtr elemPtr = IntPtr.Add(ptr, i * elemSize);
                        object? elemVal = ReadPointer(elemPtr, elemType);
                        list.Add(MapObject(elemVal, elemType, visited));
                    }
                }
                finally
                {
                    Marshal.DestroyStructure(ptr, type);
                    Marshal.FreeHGlobal(ptr);
                }
            }
            return list;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var f in fields)
        {
            var fixedAttr = f.GetCustomAttribute<FixedBufferAttribute>();
            if (fixedAttr != null)
            {
                int length = fixedAttr.Length;
                Type elemType = fixedAttr.ElementType;
                var list = new List<object?>();

                object fixedStruct = f.GetValue(obj)!;
                
                int structSize = Marshal.SizeOf(fixedStruct.GetType());
                IntPtr ptr = Marshal.AllocHGlobal(structSize);
                try
                {
                    Marshal.StructureToPtr(fixedStruct, ptr, false);
                    int elemSize = GetSizeOf(elemType);
                    
                    for (int i = 0; i < length; i++)
                    {
                        IntPtr elemPtr = IntPtr.Add(ptr, i * elemSize);
                        object? elemVal = ReadPointer(elemPtr, elemType);
                        list.Add(MapObject(elemVal, elemType, visited));
                    }
                }
                finally
                {
                    Marshal.DestroyStructure(ptr, fixedStruct.GetType());
                    Marshal.FreeHGlobal(ptr);
                }

                dict[f.Name] = list;
            }
            else
            {
                dict[f.Name] = MapObject(f.GetValue(obj), f.FieldType, visited);
            }
        }

        if (!type.IsValueType)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);
            foreach (var p in props)
            {
                try { dict[p.Name] = MapObject(p.GetValue(obj), p.PropertyType, visited); }
                catch { }
            }
        }

        return dict;
    }
    
    private static object? ReadPointer(IntPtr ptr, Type type)
    {
        if (type == typeof(byte)) return Marshal.ReadByte(ptr);
        if (type == typeof(sbyte)) return (sbyte)Marshal.ReadByte(ptr);
        if (type == typeof(short)) return Marshal.ReadInt16(ptr);
        if (type == typeof(ushort)) return unchecked((ushort)Marshal.ReadInt16(ptr));
        if (type == typeof(int)) return Marshal.ReadInt32(ptr);
        if (type == typeof(uint)) return unchecked((uint)Marshal.ReadInt32(ptr));
        if (type == typeof(long)) return Marshal.ReadInt64(ptr);
        if (type == typeof(ulong)) return unchecked((ulong)Marshal.ReadInt64(ptr));
        if (type == typeof(float)) { var arr = new float[1]; Marshal.Copy(ptr, arr, 0, 1); return arr[0]; }
        if (type == typeof(double)) { var arr = new double[1]; Marshal.Copy(ptr, arr, 0, 1); return arr[0]; }
        if (type == typeof(bool)) return Marshal.ReadByte(ptr) != 0;
        
        try { return Marshal.PtrToStructure(ptr, type); } catch { return null; }
    }
    
    private static int GetSizeOf(Type type)
    {
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        return Marshal.SizeOf(type);
    }
}
