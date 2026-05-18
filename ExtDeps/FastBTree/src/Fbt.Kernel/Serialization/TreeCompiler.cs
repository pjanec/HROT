using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fbt.Serialization
{
    /// <summary>
    /// Compiles JSON tree definitions into optimized runtime BehaviorTreeBlobs.
    /// Handles flattening, deduplication, and hashing.
    /// </summary>
    public static class TreeCompiler
    {
        /// <summary>
        /// Compiles a JSON string into a binary-ready BehaviorTreeBlob.
        /// </summary>
        public static BehaviorTreeBlob CompileFromJson(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
                throw new ArgumentException("JSON text cannot be empty", nameof(jsonText));

            // 1. Parse JSON to JsonTreeData
            JsonTreeData? treeData;
            try
            {
                treeData = JsonSerializer.Deserialize<JsonTreeData>(jsonText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Failed to parse JSON: {ex.Message}", ex);
            }
            
            if (treeData == null) throw new ArgumentException("JSON deserialized to null");
            if (string.IsNullOrEmpty(treeData.TreeName)) throw new ArgumentException("TreeName is required");
            if (treeData.Root == null) throw new ArgumentException("Root node is required");

            // 2. Build intermediate structure
            var builderRoot = new BuilderNode(treeData.Root);
            
            // 3. Flatten to blob (also hashes and validates)
            var blob = FlattenToBlob(builderRoot, treeData.TreeName);
            blob.Version = treeData.Version;

            // 4. Print any non-fatal warnings (FlattenToBlob already threw for fatal ones)
            var validation = TreeValidator.Validate(blob);
            if (validation.HasWarnings)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Tree '{blob.TreeName}' has warnings:\n{validation}");
                Console.ResetColor();
            }
            
            return blob;
        }

        /// <summary>
        /// Flattens a pre-built BuilderNode tree into a BehaviorTreeBlob.
        /// Calculates StructureHash and ParamHash, and validates the result.
        /// Throws <see cref="BehaviorTreeBuildException"/> if validation fails or if
        /// illegal nesting (nested Parallel or nested Repeater) is detected.
        /// </summary>
        public static BehaviorTreeBlob FlattenToBlob(BuilderNode root, string treeName)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(treeName)) throw new ArgumentException("treeName cannot be empty", nameof(treeName));

            // 1. Flatten the node tree
            var blob = FlattenToBlobCore(root, treeName);

            // 2. Calculate hashes
            blob.StructureHash = CalculateStructureHash(blob.Nodes);
            blob.ParamHash = CalculateParamHash(blob.FloatParams, blob.IntParams);

            // 3. Validate
            var validation = TreeValidator.Validate(blob);

            if (!validation.IsValid)
            {
                throw new BehaviorTreeBuildException(
                    $"Tree '{treeName}' failed validation:\n{validation}");
            }

            // 4. Treat nested Parallel/Repeater warnings as hard errors
            foreach (var warning in validation.Warnings)
            {
                if (warning.Contains("Nested Parallel") || warning.Contains("Nested Repeater"))
                {
                    throw new BehaviorTreeBuildException(
                        $"Tree '{treeName}' contains illegal nested node: {warning}");
                }
            }

            return blob;
        }
        
        private static BehaviorTreeBlob FlattenToBlobCore(BuilderNode root, string treeName)
        {
            var nodes = new List<NodeDefinition>();
            var methodNames = new List<string>();
            var floatParams = new List<float>();
            var intParams = new List<int>();
            
            FlattenRecursive(root, nodes, methodNames, floatParams, intParams);
            
            return new BehaviorTreeBlob
            {
                TreeName = treeName,
                Nodes = nodes.ToArray(),
                MethodNames = methodNames.ToArray(),
                FloatParams = floatParams.ToArray(),
                IntParams = intParams.ToArray()
            };
        }
        
        private static void FlattenRecursive(
            BuilderNode node,
            List<NodeDefinition> nodes,
            List<string> methodNames,
            List<float> floatParams,
            List<int> intParams)
        {
            int currentIndex = nodes.Count;
            
            // Calculate subtree offset (how many nodes in this entire subtree)
            // This includes self + all descendants
            // The SubtreeOffset field in NodeDefinition is used as a relative jump to the NEXT SIBLING.
            // If we are at index i, and SubtreeOffset is k, the next sibling is at i + k.
            // Since k is the count of nodes in the subtree, i + k points exactly to the slot AFTER the last node of this subtree.
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
            else if (node.Type == NodeType.Repeater)
            {
                payloadIndex = GetOrAddInt(intParams, node.RepeatCount);
            }
            else if (node.Type == NodeType.Cooldown)
            {
                payloadIndex = GetOrAddFloat(floatParams, node.CooldownTime);
            }
            else if (node.Type == NodeType.Parallel)
            {
                payloadIndex = GetOrAddInt(intParams, node.Policy);
            }
            
            // Add this node
            nodes.Add(new NodeDefinition
            {
                Type = node.Type,
                ChildCount = (byte)node.Children.Count,
                SubtreeOffset = (ushort)subtreeSize, // This is critical!
                PayloadIndex = payloadIndex
            });
            
            // Recursively flatten children
            foreach (var child in node.Children)
            {
                FlattenRecursive(child, nodes, methodNames, floatParams, intParams);
            }
        }
        
        private static int GetOrAddMethodName(List<string> names, string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            
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
            // Simple deduplication (exact match)
            int index = floats.IndexOf(value);
            if (index == -1)
            {
                index = floats.Count;
                floats.Add(value);
            }
            return index;
        }

        private static int GetOrAddInt(List<int> ints, int value)
        {
            int index = ints.IndexOf(value);
            if (index == -1)
            {
                index = ints.Count;
                ints.Add(value);
            }
            return index;
        }
        
        // Made internal for testing
        internal static int CalculateStructureHash(NodeDefinition[] nodes)
        {
            if (nodes == null || nodes.Length == 0) return 0;

            using var md5 = MD5.Create();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            foreach (var node in nodes)
            {
                writer.Write((byte)node.Type);
                writer.Write(node.ChildCount);
                // Note: We intentionally do NOT hash SubtreeOffset or PayloadIndex here
                // We want structure identity: "Sequence with 2 children of type Action"
                // Payload changes (different method name) should NOT change structure hash
                // Wait... if payload index changes, it might be same structure but different behavior.
                // The requirements say: "Structure hash: MD5 hash of node types only (ignores payload)"
                // But typically ChildCount is part of structure.
            }
            
            var hash = md5.ComputeHash(ms.ToArray());
            return BitConverter.ToInt32(hash, 0);
        }
        
        // Made internal for testing
        internal static int CalculateParamHash(float[] floatParams, int[] intParams)
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
    }
}
