# FastBTree Serialization & Asset Pipeline

**Version:** 1.0.0  
**Date:** 2026-01-04

---

## 1. Overview

The serialization system handles:

- **JSON to BehaviorTreeBlob** conversion (primary workflow)
- **Binary serialization** for runtime assets
- **Dependency tracking** for subtrees
- **Hot reload** support with hash validation
- **Asset compilation pipeline**

---

## 2. JSON Format Specification

### 2.1 Tree Structure

```json
{
  "treeName": "OrcCombat",
  "version": 1,
  "root": {
    "type": "Selector",
    "children": [
      {
        "type": "Sequence",
        "name": "CombatBranch",
        "children": [
          {
            "type": "Condition",
            "method": "HasTarget"
          },
          {
            "type": "Action",
            "method": "Attack"
          }
        ]
      },
      {
        "type": "Action",
        "method": "Patrol"
      }
    ]
  }
}
```

### 2.2 Node Schema

```typescript
interface JsonNode {
  // Required
  type: "Root" | "Sequence" | "Selector" | "Parallel" | "Action" | "Condition" 
        | "Inverter" | "Repeater" | "Wait" | "Observer" | "Subtree";
  
  // Optional
  name?: string;           // Debugging/documentation
  method?: string;         // For Action/Condition: method name
  children?: JsonNode[];   // For composites
  params?: {               // Node-specific parameters
    duration?: number;     // For Wait
    count?: number;        // For Repeater
    subtreePath?: string;  // For Subtree
  };
}
```

###2.3 Complete Example

```json
{
  "treeName": "OrcPatrolCombat",
  "version": 1,
  "root": {
    "type": "Selector",
    "name": "RootPriority",
    "children": [
      {
        "type": "Observer",
        "name": "CombatGuard",
        "params": { "abortMode": "LowerPriority" },
        "children": [
          {
            "type": "Condition",
            "method": "HasEnemyTarget"
          },
          {
            "type": "Selector",
            "name": "CombatTactics",
            "children": [
              {
                "type": "Sequence",
                "name": "MeleeAttack",
                "children": [
                  {
                    "type": "Condition",
                    "method": "IsTargetInRange",
                    "params": { "range": 2.0 }
                  },
                  {
                    "type": "Action",
                    "method": "AttackMelee"
                  }
                ]
              },
              {
                "type": "Sequence",
                "name": "ChaseTarget",
                "children": [
                  {
                    "type": "Action",
                    "method": "MoveToTarget"
                  },
                  {
                    "type": "Action",
                    "method": "PlayTaunt",
                    "params": { "cooldown": 5.0 }
                  }
                ]
              }
            ]
          }
        ]
      },
      {
        "type": "Sequence",
        "name": "DefaultPatrol",
        "children": [
          {
            "type": "Action",
            "method": "GetNextPatrolPoint"
          },
          {
            "type": "Action",
            "method": "MoveToLocation"
          },
          {
            "type": "Wait",
            "params": { "duration": 2.0 }
          }
        ]
      }
    ]
  }
}
```

---

## 3. Compilation Pipeline

### 3.1 BuilderNode (Intermediate)

```csharp
namespace Fbt.Tools
{
    /// <summary>
    /// Temporary recursive structure for tree building.
    /// Converted to flat NodeDefinition[] array.
    /// </summary>
    internal class BuilderNode
    {
        public NodeType Type;
        public string Name;
        public string MethodName;
        public List<BuilderNode> Children = new();
        public Dictionary<string, object> Params = new();
    }
}
```

### 3.2 TreeCompiler

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fbt.Tools
{
    /// <summary>
    /// Compiles JSON trees into BehaviorTreeBlob.
    /// </summary>
    public static class TreeCompiler
    {
        public static BehaviorTreeBlob CompileFromJson(string jsonContent)
        {
            // 1. Parser JSON
            var jsonRoot = JsonSerializer.Deserialize<JsonTreeData>(jsonContent);
            
            // 2. Build intermediate structure
            var builderRoot = ParseJsonNode(jsonRoot.Root);
            
            // 3. Flatten to NodeDefinition[]
            var blob = BuildBlob(builderRoot, jsonRoot.TreeName);
            
            // 4. Calculate hashes
            blob.StructureHash = CalculateStructureHash(blob.Nodes);
            blob.ParamHash = CalculateParamHash(blob.FloatParams, blob.IntParams);
            
            return blob;
        }
        
        private static BuilderNode ParseJsonNode(JsonNode jsonNode)
        {
            var node = new BuilderNode
            {
                Type = ParseNodeType(jsonNode.Type),
                Name = jsonNode.Name ?? jsonNode.Type,
                MethodName = jsonNode.Method
            };
            
            // Parse parameters
            if (jsonNode.Params != null)
            {
                foreach (var kvp in jsonNode.Params)
                {
                    node.Params[kvp.Key] = kvp.Value;
                }
            }
            
            // Parse children recursively
            if (jsonNode.Children != null)
            {
                foreach (var child in jsonNode.Children)
                {
                    node.Children.Add(ParseJsonNode(child));
                }
            }
            
            return node;
        }
        
        private static BehaviorTreeBlob BuildBlob(BuilderNode root, string treeName)
        {
            var blob = new BehaviorTreeBlob { TreeName = treeName };
            var flatNodes = new List<NodeDefinition>();
            
            // Registries for deduplication
            var methodRegistry = new List<string>();
            var floatParamRegistry = new List<float>();
            var intParamRegistry = new List<int>();
            
            // Recursive flattening
            void Flatten(BuilderNode current)
            {
                int myIndex = flatNodes.Count;
                
                // Resolve payload index
                int payloadIndex = -1;
                
                if (current.Type == NodeType.Action || current.Type == NodeType.Condition)
                {
                    // Register method name
                    payloadIndex = GetOrAddMethod(methodRegistry, current.MethodName);
                }
                else if (current.Type == NodeType.Wait)
                {
                    // Register duration parameter
                    float duration = current.Params.TryGetValue("duration", out var val) 
                        ? Convert.ToSingle(val) 
                        : 1.0f;
                    payloadIndex = GetOrAddFloat(floatParamRegistry, duration);
                }
                else if (current.Type == NodeType.Repeater)
                {
                    // Register repeat count
                    int count = current.Params.TryGetValue("count", out var val)
                        ? Convert.ToInt32(val)
                        : -1; // -1 = infinite
                    payloadIndex = GetOrAddInt(intParamRegistry, count);
                }
                
                // Create node definition
                var def = new NodeDefinition
                {
                    Type = current.Type,
                    ChildCount = (byte)current.Children.Count,
                    PayloadIndex = payloadIndex,
                    SubtreeOffset = 0 // Placeholder
                };
                
                flatNodes.Add(def);
                
                // Recurse children
                foreach (var child in current.Children)
                {
                    Flatten(child);
                }
                
                // Backpatch SubtreeOffset
                int nextSiblingIndex = flatNodes.Count;
                int offset = nextSiblingIndex - myIndex;
                
                var updated = flatNodes[myIndex];
                updated.SubtreeOffset = (ushort)offset;
                flatNodes[myIndex] = updated;
            }
            
            Flatten(root);
            
            // Finalize blob
            blob.Nodes = flatNodes.ToArray();
            blob.MethodNames = methodRegistry.ToArray();
            blob.FloatParams = floatParamRegistry.ToArray();
            blob.IntParams = intParamRegistry.ToArray();
            
            return blob;
        }
        
        private static int GetOrAddMethod(List<string> registry, string name)
        {
            int index = registry.IndexOf(name);
            if (index == -1)
            {
                index = registry.Count;
                registry.Add(name);
            }
            return index;
        }
        
        private static int GetOrAddFloat(List<float> registry, float value)
        {
            int index = registry.IndexOf(value);
            if (index == -1)
            {
                index = registry.Count;
                registry.Add(value);
            }
            return index;
        }
        
        private static int GetOrAddInt(List<int> registry, int value)
        {
            int index = registry.IndexOf(value);
            if (index == -1)
            {
                index = registry.Count;
                registry.Add(value);
            }
            return index;
        }
        
        // === HASH CALCULATION ===
        
        private static int CalculateStructureHash(NodeDefinition[] nodes)
        {
            using var md5 = MD5.Create();
            var builder = new StringBuilder();
            
            foreach (var node in nodes)
            {
                builder.Append($"{(int)node.Type}:{node.ChildCount}:");
            }
            
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = md5.ComputeHash(bytes);
            return BitConverter.ToInt32(hash, 0);
        }
        
        private static int CalculateParamHash(float[] floats, int[] ints)
        {
            using var md5 = MD5.Create();
            var builder = new StringBuilder();
            
            foreach (var f in floats)
                builder.Append($"{f}:");
            foreach (var i in ints)
                builder.Append($"{i}:");
            
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = md5.ComputeHash(bytes);
            return BitConverter.ToInt32(hash, 0);
        }
        
        private static NodeType ParseNodeType(string typeName)
        {
            return typeName.ToLower() switch
            {
                "root" => NodeType.Root,
                "sequence" => NodeType.Sequence,
                "selector" => NodeType.Selector,
                "fallback" => NodeType.Selector, // Alias
                "parallel" => NodeType.Parallel,
                "action" => NodeType.Action,
                "condition" => NodeType.Condition,
                "inverter" => NodeType.Inverter,
                "repeater" => NodeType.Repeater,
                "wait" => NodeType.Wait,
                "observer" => NodeType.Observer,
                "subtree" => NodeType.Subtree,
                _ => throw new Exception($"Unknown node type: {typeName}")
            };
        }
    }
    
    // === JSON DATA CLASSES ===
    
    internal class JsonTreeData
    {
        public string TreeName { get; set; }
        public int Version { get; set; }
        public JsonNode Root { get; set; }
    }
    
    internal class JsonNode
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Method { get; set; }
        public JsonNode[] Children { get; set; }
        public Dictionary<string, object> Params { get; set; }
    }
}
```

---

## 4. Binary Serialization

### 4.1 Format Specification

```
┌─────────────────────────────────────────┐
│ Header                                  │
│  - Magic: "FBTREE" (6 bytes)            │
│  - Version: int32 (4 bytes)             │
│  - StructureHash: int32 (4 bytes)       │
│  - ParamHash: int32 (4 bytes)           │
│  - TreeName: string (length-prefixed)   │
├─────────────────────────────────────────┤
│ Nodes Array                             │
│  - Count: int32                         │
│  - Nodes: NodeDefinition[Count]         │
│    (8 bytes each, direct memcpy)        │
├─────────────────────────────────────────┤
│ MethodNames Array                       │
│  - Count: int32                         │
│  - Strings: string[Count]               │
├─────────────────────────────────────────┤
│ FloatParams Array                       │
│  - Count: int32                         │
│  - Values: float[Count]                 │
├─────────────────────────────────────────┤
│ IntParams Array                         │
│  - Count: int32                         │
│  - Values: int[Count]                   │
└─────────────────────────────────────────┘
```

### 4.2 Writer Implementation

```csharp
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Fbt.Serialization
{
    public static class BinaryTreeSerializer
    {
        private const string Magic = "FBTREE";
        
        public static void Save(Stream stream, BehaviorTreeBlob blob)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            
            // Header
            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write(blob.Version);
            writer.Write(blob.StructureHash);
            writer.Write(blob.ParamHash);
            writer.Write(blob.TreeName);
            
            // Nodes (bulk write)
            writer.Write(blob.Nodes.Length);
            var nodeBytes = MemoryMarshal.AsBytes(blob.Nodes.AsSpan());
            writer.Write(nodeBytes);
            
            // Method Names
            WriteStringArray(writer, blob.MethodNames);
            
            // Float Params
            WriteFloatArray(writer, blob.FloatParams);
            
            // Int Params
            WriteIntArray(writer, blob.IntParams);
        }
        
        public static BehaviorTreeBlob Load(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            
            // Validate magic
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(6));
            if (magic != Magic)
                throw new InvalidDataException($"Invalid magic: {magic}");
            
            var blob = new BehaviorTreeBlob();
            
            // Header
            blob.Version = reader.ReadInt32();
            blob.StructureHash = reader.ReadInt32();
            blob.ParamHash = reader.ReadInt32();
            blob.TreeName = reader.ReadString();
            
            // Nodes
            int nodeCount = reader.ReadInt32();
            blob.Nodes = new NodeDefinition[nodeCount];
            var nodeBytes = MemoryMarshal.AsBytes(blob.Nodes.AsSpan());
            reader.Read(nodeBytes);
            
            // Tables
            blob.MethodNames = ReadStringArray(reader);
            blob.FloatParams = ReadFloatArray(reader);
            blob.IntParams = ReadIntArray(reader);
            
            return blob;
        }
        
        private static void WriteStringArray(BinaryWriter w, string[] arr)
        {
            w.Write(arr.Length);
            foreach (var s in arr)
                w.Write(s);
        }
        
        private static string[] ReadStringArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var arr = new string[count];
            for (int i = 0; i < count; i++)
                arr[i] = r.ReadString();
            return arr;
        }
        
        private static void WriteFloatArray(BinaryWriter w, float[] arr)
        {
            w.Write(arr.Length);
            foreach (var f in arr)
                w.Write(f);
        }
        
        private static float[] ReadFloatArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var arr = new float[count];
            for (int i = 0; i < count; i++)
                arr[i] = r.ReadSingle();
            return arr;
        }
        
        private static void WriteIntArray(BinaryWriter w, int[] arr)
        {
            w.Write(arr.Length);
            foreach (var i in arr)
                w.Write(i);
        }
        
        private static int[] ReadIntArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var arr = new int[count];
            for (int i = 0; i < count; i++)
                arr[i] = r.ReadInt32();
            return arr;
        }
    }
}
```

---

## 5. Dependency Tracking

### 5.1 Dependency Database

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Fbt.Tools
{
    /// <summary>
    /// Tracks which parent trees depend on which subtrees.
    /// Used for automatic rebuild when subtrees change.
    /// </summary>
    public class DependencyDatabase
    {
        private Dictionary<string, HashSet<string>> _dependents = new();
        
        public static DependencyDatabase Instance { get; } = new();
        
        public void RegisterDependency(string parentPath, string subtreePath)
        {
            if (!_dependents.ContainsKey(subtreePath))
                _dependents[subtreePath] = new HashSet<string>();
            
            _dependents[subtreePath].Add(parentPath);
        }
        
        public IEnumerable<string> GetDependents(string subtreePath)
        {
            if (_dependents.TryGetValue(subtreePath, out var deps))
                return deps;
            return Array.Empty<string>();
        }
        
        public void SaveToFile(string path)
        {
            var json = JsonSerializer.Serialize(_dependents);
            File.WriteAllText(path, json);
        }
        
        public void LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            _dependents = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json);
        }
    }
}
```

### 5.2 Auto-Rebuild System

```csharp
namespace Fbt.Tools
{
    /// <summary>
    /// Watches for file changes and triggers rebuilds.
    /// </summary>
    public class AssetWatcher
    {
        public static void OnFileChanged(string changedFilePath)
        {
            if (!changedFilePath.EndsWith(".json"))
                return;
            
            // 1. Rebuild the changed file
            Console.WriteLine($"Rebuilding {changedFilePath}...");
            RebuildAsset(changedFilePath);
            
            // 2. Find dependents
            var deps = DependencyDatabase.Instance.GetDependents(changedFilePath);
            
            // 3. Rebuild dependents (cascading)
            foreach (var depPath in deps)
            {
                Console.WriteLine($"Rebuilding dependent {depPath}...");
                RebuildAsset(depPath);
            }
        }
        
        private static void RebuildAsset(string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var blob = TreeCompiler.CompileFromJson(json);
            
            // Save binary
            string binPath = Path.ChangeExtension(jsonPath, ".fbtree");
            using (var stream = File.Create(binPath))
            {
                BinaryTreeSerializer.Save(stream, blob);
            }
        }
    }
}
```

---

## 6. Validation

### 6.1 TreeValidator

```csharp
namespace Fbt.Tools
{
    public static class TreeValidator
    {
        public static ValidationResult Validate(BehaviorTreeBlob blob)
        {
            var result = new ValidationResult();
            
            // Check root
            if (blob.Nodes.Length == 0)
            {
                result.AddError("Tree is empty");
                return result;
            }
            
            // Validate each node
            for (int i = 0; i < blob.Nodes.Length; i++)
            {
                ref var node = ref blob.Nodes[i];
                
                // Validate SubtreeOffset
                int nextSibling = i + node.SubtreeOffset;
                if (nextSibling < i || nextSibling > blob.Nodes.Length)
                {
                    result.AddError($"Node {i}: Invalid SubtreeOffset ({node.SubtreeOffset})");
                }
                
                // Validate PayloadIndex
                if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
                {
                    if (node.PayloadIndex < 0 || node.PayloadIndex >= blob.MethodNames.Length)
                    {
                        result.AddError($"Node {i}: Invalid method index ({node.PayloadIndex})");
                    }
                }
                
                // Validate Children
                if (node.ChildCount > 0 && i + 1 >= blob.Nodes.Length)
                {
                    result.AddError($"Node {i}: Has children but is last node");
                }
            }
            
            return result;
        }
    }
    
    public class ValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        
        public bool IsValid => Errors.Count == 0;
        
        public void AddError(string message) => Errors.Add(message);
        public void AddWarning(string message) => Warnings.Add(message);
    }
}
```

---

**Next Document:** `05-Testing-Strategy.md`
