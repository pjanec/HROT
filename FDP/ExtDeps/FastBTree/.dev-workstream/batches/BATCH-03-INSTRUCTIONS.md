# BATCH-03: Serialization & Asset Pipeline

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Batch Number:** 03  
**Phase:** Phase 1 - Core (Week 3)  
**Assigned:** 2026-01-04  
**Estimated Effort:** 6-8 days  
**Prerequisites:** BATCH-01 ✅, BATCH-02 ✅

---

## 📋 Batch Overview

This batch implements the **serialization and asset pipeline** - transforming human-authored JSON trees into efficient runtime bytecode. You will:

1. Design and implement custom JSON tree format
2. Build the tree compiler (JSON → B Blob)
3. Implement depth-first flattening algorithm
4. Add binary serialization for storage
5. Implement tree validation
6. Create hash-based hot reload detection
7. Build integration tests with full tree execution

**Critical Success Factors:**
- ✅ Correct SubtreeOffset calculation (critical for execution)
- ✅ Method/parameter deduplication (memory efficiency)
- ✅ Hash-based change detection (enables hot reload)
- ✅ Full compilation pipeline tested end-to-end
- ✅ Integration tests validate executable trees

---

## 📚 Required Reading

**BEFORE starting, read these design documents:**

1. **[docs/design/04-Serialization.md](../../docs/design/04-Serialization.md)** - Full serialization design (CRITICAL!)
2. **[docs/design/01-Data-Structures.md § 4](../../docs/design/01-Data-Structures.md)** - BehaviorTreeBlob structure
3. **[docs/design/02-Execution-Model.md § 2](../../docs/design/02-Execution-Model.md)** - How bytecode is executed

**Key Concepts to Understand:**
- Depth-first flattening vs. JSON hierarchy
- SubtreeOffset backpatching
- Lookup table deduplication
- Structure vs. param hashing

---

## 🎯 Tasks

### Task 1: JSON Tree Format Definition

**Objective:** Define the JSON structure for authoring behavior trees.

**File:** `src/Fbt.Kernel/Serialization/JsonTreeData.cs`

**Specification:** See [04-Serialization.md § 2.1](../../docs/design/04-Serialization.md#21-json-format)

**Acceptance Criteria:**
- [x] `JsonTreeData` class with properties:
  - `string TreeName`
  - `int Version`
  - `JsonNode Root`
- [x] `JsonNode` class (recursive structure):
  - `string Type` - Node type name ("Sequence", "Action", etc.)
  - `string Action` - Method name (for Action/Condition nodes)
  - `float WaitTime` - Duration (for Wait nodes)
  - `int RepeatCount` - Count (for Repeater nodes)
  - `JsonNode[] Children` - Child nodes
- [x] XML documentation on all properties

**Example JSON:**
```json
{
  "TreeName": "OrcPatrol",
  "Version": 1,
  "Root": {
    "Type": "Sequence",
    "Children": [
      {
        "Type": "Action",
        "Action": "FindRandomPoint"
      },
      {
        "Type": "Action",
        "Action": "MoveTo"
      },
      {
        "Type": "Wait",
        "WaitTime": 2.0
      }
    ]
  }
}
```

**Notes:**
- Keep structure simple for now (no complex decorators yet)
- Focus on Sequence, Selector, Action, Wait, Inverter

---

### Task 2: Tree Compiler - JSON Parsing

**Objective:** Parse JSON into JsonTreeData objects.

**File:** `src/Fbt.Kernel/Serialization/TreeCompiler.cs`

**Specification:** See [04-Serialization.md § 3.1](../../docs/design/04-Serialization.md#31-json-parsing)

**Acceptance Criteria:**
- [x] Static method: `BehaviorTreeBlob CompileFromJson(string jsonText)`
- [x] Uses `System.Text.Json.JsonSerializer` for parsing
- [x] Validates tree name is not empty
- [x] Validates root node exists
- [x] Returns compiled `BehaviorTreeBlob`

**Implementation:**
```csharp
public static class TreeCompiler
{
    public static BehaviorTreeBlob CompileFromJson(string jsonText)
    {
        // 1. Parse JSON to JsonTreeData
        var treeData = JsonSerializer.Deserialize<JsonTreeData>(jsonText);
        
        // 2. Build intermediate structure
        var builderRoot = BuildFromJson(treeData.Root);
        
        // 3. Flatten to blob
        var blob = FlattenToBlob(builderRoot, treeData.TreeName);
        
        // 4. Calculate hashes
        blob.StructureHash = CalculateStructureHash(blob.Nodes);
        blob.ParamHash = CalculateParamHash(blob.FloatParams, blob.IntParams);
        
        return blob;
    }
}
```

**Tests:**
- Parse simple sequence tree
- Parse tree with wait nodes
- Parse tree with nested selectors

---

### Task 3: BuilderNode Intermediate Structure

**Objective:** Create intermediate representation for tree compilation.

**File:** `src/Fbt.Kernel/Serialization/BuilderNode.cs`

**Specification:** See [04-Serialization.md § 3.2](../../docs/design/04-Serialization.md#32-buildingnode-intermediate)

**Acceptance Criteria:**
- [x] `BuilderNode` class with properties:
  - `NodeType Type`
  - `string MethodName` (for Action/Condition)
  - `float WaitTime` (for Wait)
  - `int RepeatCount` (for Repeater)
  - `List<BuilderNode> Children`
- [x] Constructor from `JsonNode`
- [x] Helper method: `int CalculateSubtreeSize()` - Returns total nodes in subtree (self + all descendants)

**Implementation:**
```csharp
public class BuilderNode
{
    public NodeType Type { get; set; }
    public string MethodName { get; set; }
    public float WaitTime { get; set; }
    public int RepeatCount { get; set; }
    public List<BuilderNode> Children { get; } = new List<BuilderNode>();
    
    public BuilderNode(JsonNode jsonNode)
    {
        // Map string Type to NodeType enum
        Type = MapNodeType(jsonNode.Type);
        
        // Extract node-specific data
        if (Type == NodeType.Action || Type == NodeType.Condition)
            MethodName = jsonNode.Action;
        else if (Type == NodeType.Wait)
            WaitTime = jsonNode.WaitTime;
        
        // Recursively build children
        if (jsonNode.Children != null)
        {
            foreach (var child in jsonNode.Children)
                Children.Add(new BuilderNode(child));
        }
    }
    
    public int CalculateSubtreeSize()
    {
        int size = 1; // Self
        foreach (var child in Children)
            size += child.CalculateSubtreeSize();
        return size;
    }
    
    private static NodeType MapNodeType(string typeName)
    {
        return typeName switch
        {
            "Sequence" => NodeType.Sequence,
            "Selector" => NodeType.Selector,
            "Action" => NodeType.Action,
            "Condition" => NodeType.Condition,
            "Wait" => NodeType.Wait,
            "Inverter" => NodeType.Inverter,
            _ => throw new ArgumentException($"Unknown node type: {typeName}")
        };
    }
}
```

---

### Task 4: Depth-First Flattening

**Objective:** Convert hierarchical BuilderNode tree to flat NodeDefinition array.

**Method:** `TreeCompiler.FlattenToBlob(BuilderNode root, string treeName)`

**Specification:** See [04-Serialization.md § 3.3](../../docs/design/04-Serialization.md#33-depth-first-flattening)

**Acceptance Criteria:**
- [x] Flattens tree depth-first (parent, then children left-to-right)
- [x] Correctly calculates `SubtreeOffset` for each node
- [x] Deduplicates method names into `MethodNames[]`
- [x] Deduplicates float params into `FloatParams[]`
- [x] Assigns correct `PayloadIndex` for actions/waits
- [x] Sets `ChildCount` for composites

**Critical Algorithm:**
```csharp
private static BehaviorTreeBlob FlattenToBlob(BuilderNode root, string treeName)
{
    var nodes = new List<NodeDefinition>();
    var methodNames = new List<string>();
    var floatParams = new List<float>();
    
    FlattenRecursive(root, nodes, methodNames, floatParams);
    
    return new BehaviorTreeBlob
    {
        TreeName = treeName,
        Nodes = nodes.ToArray(),
        MethodNames = methodNames.ToArray(),
        FloatParams = floatParams.ToArray(),
        IntParams = Array.Empty<int>() // Placeholder for now
    };
}

private static void FlattenRecursive(
    BuilderNode node,
    List<NodeDefinition> nodes,
    List<string> methodNames,
    List<float> floatParams)
{
    int currentIndex = nodes.Count;
    
    // Calculate subtree offset (how many nodes in this entire subtree)
    int subtreeSize = node.CalculateSubtreeSize();
    
    // Determine payload index
    int payloadIndex = -1;
    if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
    {
        payloadIndex = GetOrAddMethodName(methodNames, node.MethodName);
    }
    else if (node.Type == NodeType.Wait)
    {
        payloadIndex = GetOrAddFloat(floatParams, node.WaitTime);
    }
    
    // Add this node
    nodes.Add(new NodeDefinition
    {
        Type = node.Type,
        ChildCount = (byte)node.Children.Count,
        SubtreeOffset = (ushort)subtreeSize,
        PayloadIndex = payloadIndex
    });
    
    // Recursively flatten children
    foreach (var child in node.Children)
    {
        FlattenRecursive(child, nodes, methodNames, floatParams);
    }
}

private static int GetOrAddMethodName(List<string> names, string name)
{
    int index = names.IndexOf(name);
    if (index == -1)
    {
        index = names.Count;
        names.Add(name);
    }
    return index;
}

private static int GetOrAddFloat(List<float> floats, float value)
{
    // For now, simple append (could deduplicate if needed)
    int index = floats.Count;
    floats.Add(value);
    return index;
}
```

**Tests Required:**
```csharp
[Fact]
public void FlattenToBlob_SimpleSequence_CorrectSubtreeOffsets()

[Fact]
public void FlattenToBlob_NestedTrees_CorrectOffsets()

[Fact]
public void FlattenToBlob_DeduplicateMethodNames()

[Fact]
public void FlattenToBlob_WaitNode_StoresFloatParam()
```

---

### Task 5: Hash Calculation

**Objective:** Generate structure and parameter hashes for hot reload.

**Methods in TreeCompiler:**
- `int CalculateStructureHash(NodeDefinition[] nodes)`
- `int CalculateParamHash(float[] floatParams, int[] intParams)`

**Specification:** See [04-Serialization.md § 3.4](../../docs/design/04-Serialization.md#34-hash-calculation)

**Acceptance Criteria:**
- [x] Structure hash: MD5 hash of node types only (ignores payload)
- [x] Param hash: MD5 hash of float and int parameters
- [x] Deterministic (same tree produces same hash)
- [x] Changes in structure change structure hash
- [x] Changes in params change param hash (but not structure hash)

**Implementation:**
```csharp
using System.Security.Cryptography;

private static int CalculateStructureHash(NodeDefinition[] nodes)
{
    using var md5 = MD5.Create();
    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);
    
    foreach (var node in nodes)
    {
        writer.Write((byte)node.Type);
        writer.Write(node.ChildCount);
    }
    
    var hash = md5.ComputeHash(ms.ToArray());
    return BitConverter.ToInt32(hash, 0);
}

private static int CalculateParamHash(float[] floatParams, int[] intParams)
{
    if ((floatParams == null || floatParams.Length == 0) && 
        (intParams == null || intParams.Length == 0))
        return 0;
    
    using var md5 = MD5.Create();
    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);
    
    if (floatParams != null)
        foreach (var f in floatParams)
            writer.Write(f);
    
    if (intParams != null)
        foreach (var i in intParams)
            writer.Write(i);
    
    var hash = md5.ComputeHash(ms.ToArray());
    return BitConverter.ToInt32(hash, 0);
}
```

**Tests:**
```csharp
[Fact]
public void CalculateStructureHash_SameTree_SameHash()

[Fact]
public void CalculateStructureHash_DifferentStructure_DifferentHash()

[Fact]
public void CalculateParamHash_SameParams_SameHash()

[Fact]
public void CalculateParamHash_DifferentParams_DifferentHash()
```

---

### Task 6: Binary Serialization

**Objective:** Save and load blobs to/from binary files.

**File:** `src/Fbt.Kernel/Serialization/BinaryTreeSerializer.cs`

**Specification:** See [04-Serialization.md § 4.1](../../docs/design/04-Serialization.md#41-binary-format)

**Acceptance Criteria:**
- [x] Static method: `void Save(BehaviorTreeBlob blob, string filePath)`
- [x] Static method: `BehaviorTreeBlob Load(string filePath)`
- [x] Binary format includes:
  - Magic bytes: `FBT\0` (4 bytes)
  - Version: int (4 bytes)
  - Hashes: StructureHash, ParamHash (8 bytes)
  - Node count, then NodeDefinition array
  - Lookup table counts, then each table
- [x] Validation: Check magic bytes and version on load

**Binary Format:**
```
Offset  Size  Field
0-3     4     Magic ('F', 'B', 'T', '\0')
4-7     4     Version
8-11    4     StructureHash
12-15   4     ParamHash
16-19   4     NodeCount
20+     var   Nodes (8 bytes each)
...     4     MethodNamesCount
...     var   MethodNames (length-prefixed strings)
...     4     FloatParamsCount
...     var   FloatParams (4 bytes each)
...     4     IntParamsCount
...     var   IntParams (4 bytes each)
```

**Implementation:**
```csharp
public static class BinaryTreeSerializer
{
    private static readonly byte[] MagicBytes = { (byte)'F', (byte)'B', (byte)'T', 0 };
    private const int CurrentVersion = 1;
    
    public static void Save(BehaviorTreeBlob blob, string filePath)
    {
        using var fs = File.Create(filePath);
        using var writer = new BinaryWriter(fs);
        
        // Header
        writer.Write(MagicBytes);
        writer.Write(CurrentVersion);
        writer.Write(blob.StructureHash);
        writer.Write(blob.ParamHash);
        
        // Nodes
        writer.Write(blob.Nodes.Length);
        foreach (var node in blob.Nodes)
        {
            writer.Write((byte)node.Type);
            writer.Write(node.ChildCount);
            writer.Write(node.SubtreeOffset);
            writer.Write(node.PayloadIndex);
        }
        
        // Method names
        writer.Write(blob.MethodNames?.Length ?? 0);
        if (blob.MethodNames != null)
            foreach (var name in blob.MethodNames)
                writer.Write(name);
        
        // Float params
        writer.Write(blob.FloatParams?.Length ?? 0);
        if (blob.FloatParams != null)
            foreach (var f in blob.FloatParams)
                writer.Write(f);
        
        // Int params
        writer.Write(blob.IntParams?.Length ?? 0);
        if (blob.IntParams != null)
            foreach (var i in blob.IntParams)
                writer.Write(i);
    }
    
    public static BehaviorTreeBlob Load(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs);
        
        // Validate header
        var magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual(MagicBytes))
            throw new InvalidDataException("Invalid magic bytes");
        
        var version = reader.ReadInt32();
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported version: {version}");
        
        var blob = new BehaviorTreeBlob
        {
            StructureHash = reader.ReadInt32(),
            ParamHash = reader.ReadInt32()
        };
        
        // Read nodes
        int nodeCount = reader.ReadInt32();
        blob.Nodes = new NodeDefinition[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            blob.Nodes[i] = new NodeDefinition
            {
                Type = (NodeType)reader.ReadByte(),
                ChildCount = reader.ReadByte(),
                SubtreeOffset = reader.ReadUInt16(),
                PayloadIndex = reader.ReadInt32()
            };
        }
        
        // Read method names
        int methodCount = reader.ReadInt32();
        blob.MethodNames = new string[methodCount];
        for (int i = 0; i < methodCount; i++)
            blob.MethodNames[i] = reader.ReadString();
        
        // Read float params
        int floatCount = reader.ReadInt32();
        blob.FloatParams = new float[floatCount];
        for (int i = 0; i < floatCount; i++)
            blob.FloatParams[i] = reader.ReadSingle();
        
        // Read int params
        int intCount = reader.ReadInt32();
        blob.IntParams = new int[intCount];
        for (int i = 0; i < intCount; i++)
            blob.IntParams[i] = reader.ReadInt32();
        
        return blob;
    }
}
```

**Tests:**
```csharp
[Fact]
public void BinarySerializer_SaveLoad_RoundTrips()

[Fact]
public void BinarySerializer_Load_InvalidMagic_Throws()

[Fact]
public void BinarySerializer_Load_UnsupportedVersion_Throws()
```

---

### Task 7: Tree Validation

**Objective:** Validate blob correctness before execution.

**File:** `src/Fbt.Kernel/Serialization/TreeValidator.cs`

**Specification:** See [04-Serialization.md § 5.1](../../docs/design/04-Serialization.md#51-tree-validator)

**Acceptance Criteria:**
- [x] Static method: `ValidationResult Validate(BehaviorTreeBlob blob)`
- [x] Returns `ValidationResult` with `bool IsValid` and `List<string> Errors`
- [x] Validates:
  - SubtreeOffset within bounds
  - PayloadIndex within lookup table bounds
  - ChildCount matches actual children
  - No cycles (each node's subtree offset moves forward)

**Implementation:**
```csharp
public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new List<string>();
}

public static class TreeValidator
{
    public static ValidationResult Validate(BehaviorTreeBlob blob)
    {
        var result = new ValidationResult();
        
        if (blob.Nodes == null || blob.Nodes.Length == 0)
        {
            result.Errors.Add("Tree has no nodes");
            return result;
        }
        
        for (int i = 0; i < blob.Nodes.Length; i++)
        {
            var node = blob.Nodes[i];
            
            // Validate subtree offset
            if (node.SubtreeOffset == 0)
            {
                result.Errors.Add($"Node {i}: SubtreeOffset is zero");
            }
            else if (i + node.SubtreeOffset > blob.Nodes.Length)
            {
                result.Errors.Add($"Node {i}: SubtreeOffset {node.SubtreeOffset} exceeds tree bounds");
            }
            
            // Validate payload index
            if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
            {
                if (node.PayloadIndex < 0 || node.PayloadIndex >= (blob.MethodNames?.Length ?? 0))
                {
                    result.Errors.Add($"Node {i}: Invalid method PayloadIndex {node.PayloadIndex}");
                }
            }
            else if (node.Type == NodeType.Wait)
            {
                if (node.PayloadIndex < 0 || node.PayloadIndex >= (blob.FloatParams?.Length ?? 0))
                {
                    result.Errors.Add($"Node {i}: Invalid float PayloadIndex {node.PayloadIndex}");
                }
            }
        }
        
        return result;
    }
}
```

**Tests:**
```csharp
[Fact]
public void TreeValidator_ValidTree_NoErrors()

[Fact]
public void TreeValidator_InvalidSubtreeOffset_ReportsError()

[Fact]
public void TreeValidator_InvalidPayloadIndex_ReportsError()
```

---

### Task 8: Integration Tests - Full Pipeline

**Objective:** Test complete JSON → Execution pipeline.

**File:** `tests/Fbt.Tests/Integration/TreeExecutionTests.cs`

**Acceptance Criteria:**
- [x] Test: Load JSON, compile, execute with interpreter
- [x] Test: Verify tree executes correctly (all actions called)
- [x] Test: Binary save/load round-trip, then execute
- [x] Test: Hash change detection (structure vs. params)

**Required Tests:**
```csharp
[Fact]
public void IntegrationTest_SimpleSequence_ExecutesCorrectly()
{
    // JSON
    string json = @"{
        ""TreeName"": ""TestTree"",
        ""Version"": 1,
        ""Root"": {
            ""Type"": ""Sequence"",
            ""Children"": [
                { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" },
                { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" }
            ]
        }
    }";
    
    // Compile
    var blob = TreeCompiler.CompileFromJson(json);
    
    // Execute
    var registry = new ActionRegistry<TestBlackboard, MockContext>();
    registry.Register("IncrementCounter", TestActions.IncrementCounter);
    
    var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
    var bb = new TestBlackboard();
    var state = new BehaviorTreeState();
    var ctx = new MockContext();
    
    var result = interpreter.Tick(ref bb, ref state, ref ctx);
    
    Assert.Equal(NodeStatus.Success, result);
    Assert.Equal(2, bb.Counter); // Both actions executed
}

[Fact]
public void IntegrationTest_SaveLoadBinary_ExecutesSame()

[Fact]
public void IntegrationTest_StructureHashChange_DetectedDifferent()

[Fact]
public void IntegrationTest_ParamHashChange_DetectedDifferent()
```

**Minimum Integration Tests:** 5+

---

## 📊 Deliverables

### Code Files (Must Create)

**Source (src/Fbt.Kernel/Serialization/):**
- [ ] `JsonTreeData.cs`
- [ ] `JsonNode.cs`
- [ ] `BuilderNode.cs`
- [ ] `TreeCompiler.cs`
- [ ] `BinaryTreeSerializer.cs`
- [ ] `TreeValidator.cs`
- [ ] `ValidationResult.cs`

**Tests (tests/Fbt.Tests/):**
- [ ] `Unit/SerializationTests.cs` (JSON parsing, flattening, hashing)
- [ ] `Unit/BinarySerializerTests.cs`
- [ ] `Unit/TreeValidatorTests.cs`
- [ ] `Integration/TreeExecutionTests.cs` (full pipeline)

### Test Coverage

**Minimum Required:**
- [x] JSON parsing: 3+ tests
- [x] Flattening: 5+ tests (SubtreeOffset critical!)
- [x] Hashing: 4+ tests
- [x] Binary serialization: 3+ tests
- [x] Validation: 3+ tests
- [x] Integration: 5+ tests (JSON → Execute)

**Total Test Count Expected:** ~25+ new tests

---

## ✅ Definition of Done

**Batch is DONE when:**

1. **Code Quality**
   - [x] All source files created and compile
   - [x] Zero compiler warnings
   - [x] XML documentation on all public APIs

2. **Functionality**
   - [x] JSON trees parse correctly
   - [x] Flattening produces correct SubtreeOffsets
   - [x] Method names deduplicated
   - [x] Binary serialization round-trips
   - [x] Hash calculation is deterministic
   - [x] Validation detects errors

3. **Integration**
   - [x] Can compile JSON tree
   - [x] Can execute compiled tree
   - [x] All actions execute in correct order
   - [x] Structure/param hashes work

4. **Testing**
   - [x] All tests passing (100% pass rate)
   - [x] Minimum 25 new tests
   - [x] Critical paths tested (SubtreeOffset calculation!)

---

## 🚨 Critical Notes

### SubtreeOffset is CRITICAL

**This is the most error-prone part!**

```csharp
// SubtreeOffset = Total nodes in this subtree (self + all descendants)
// For a Sequence with 2 children:
//   Index 0: Sequence (SubtreeOffset = 3)  ← Total: 1 self + 2 children
//   Index 1: Action  (SubtreeOffset = 1)   ← Total: 1 self
//   Index 2: Action  (SubtreeOffset = 1)   ← Total: 1 self

// Next sibling calculation:
// NextSiblingIndex = CurrentIndex + CurrentNode.SubtreeOffset
```

**Test this extensively! If wrong, interpreter will crash or skip nodes.**

### Hash Determinism

**Hashes MUST be deterministic:**
- Same tree structure → same structure hash
- Same parameters → same param hash
- Order matters! Use depth-first traversal consistently

### Deduplication Strategy

- **Method names:** Always deduplicate (save memory)
- **Float params:** Optional (simple append for now is OK)
- **Int params:** Optional

---

## 📝 Reporting Requirements

When complete, create: `.dev-workstream/reports/BATCH-03-REPORT.md`

**Must include:**
1. Executive summary
2. Task completion table
3. Files created
4. Test results
5. **SubtreeOffset validation** (critical section!)
6. Integration test results
7. Known issues

---

## ❓ Questions?

**For Questions:**
- Create: `.dev-workstream/reports/BATCH-03-QUESTIONS.md`

**For Blockers:**
- Update: `.dev-workstream/reports/BLOCKERS-ACTIVE.md`

---

## 🎯 Success Criteria Summary

**You succeed when:**
- ✅ 7 source files created
- ✅ 4 test files created
- ✅ All tests passing (≥25 tests)
- ✅ Zero warnings
- ✅ JSON → Blob → Execute pipeline works
- ✅ SubtreeOffset calculation verified correct
- ✅ Integration tests demonstrate full functionality

**Estimated Time:** 6-8 days (this is substantial!)

---

**This completes Phase 1 core library! The three pillars (structure, execution, serialization) will be in place. 🚀**

*Batch Issued: 2026-01-04*  
*Development Leader: FastBTree Team Lead*  
*Prerequisites: BATCH-01 ✅, BATCH-02 ✅*
