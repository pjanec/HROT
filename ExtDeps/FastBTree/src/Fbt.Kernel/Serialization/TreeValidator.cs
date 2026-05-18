using System.Collections.Generic;
using System.Text;

namespace Fbt.Serialization
{
    public class ValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public bool HasWarnings => Warnings.Count > 0;
        
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        
        public override string ToString()
        {
            var sb = new StringBuilder();
            
            if (Errors.Count > 0)
            {
                sb.AppendLine($"ERRORS ({Errors.Count}):");
                foreach (var error in Errors)
                    sb.AppendLine($"  - {error}");
            }
            
            if (Warnings.Count > 0)
            {
                sb.AppendLine($"WARNINGS ({Warnings.Count}):");
                foreach (var warning in Warnings)
                    sb.AppendLine($"  - {warning}");
            }
            
            return sb.ToString();
        }
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
                else if (node.Type == NodeType.Wait || node.Type == NodeType.Cooldown)
                {
                    if (node.PayloadIndex < 0 || node.PayloadIndex >= (blob.FloatParams?.Length ?? 0))
                    {
                        result.Errors.Add($"Node {i}: Invalid float PayloadIndex {node.PayloadIndex}");
                    }
                }
                else if (node.Type == NodeType.Repeater || node.Type == NodeType.Parallel)
                {
                    if (node.PayloadIndex < 0 || node.PayloadIndex >= (blob.IntParams?.Length ?? 0))
                    {
                        result.Errors.Add($"Node {i}: Invalid int PayloadIndex {node.PayloadIndex}");
                    }
                }
                
                // NEW: Warn about Parallel child limit
                if (node.Type == NodeType.Parallel && node.ChildCount > 16)
                {
                    result.Warnings.Add(
                        $"Node {i}: Parallel has {node.ChildCount} children (max 16 supported). " +
                        "Only first 16 will execute!");
                }
            }
            
            // NEW: Detect nested Parallel nodes
            DetectNestedParallel(blob, 0, false, result);
            
            // NEW: Detect nested Repeater nodes
            DetectNestedRepeater(blob, 0, false, result);
            
            return result;
        }

        private static void DetectNestedParallel(
            BehaviorTreeBlob blob,
            int nodeIndex,
            bool insideParallel,
            ValidationResult result)
        {
            if (nodeIndex >= blob.Nodes.Length) return;
            var node = blob.Nodes[nodeIndex];
            
            // Use local variable to track if *this* scope is inside parallel, 
            // but we need to track if we were *already* inside parallel coming in.
            // If current node IS Parallel, and we are ALREADY inside one, that's a warning.
            
            if (node.Type == NodeType.Parallel)
            {
                if (insideParallel)
                {
                    result.Warnings.Add(
                        $"Node {nodeIndex}: Nested Parallel detected! " +
                        "Both Parallel nodes will conflict on LocalRegisters[3]. " +
                        "This will cause incorrect execution. Consider restructuring the tree.");
                }
                insideParallel = true; // Mark we're inside Parallel (for children)
            }
            
            // Recursively check children
            int childIndex = nodeIndex + 1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                DetectNestedParallel(blob, childIndex, insideParallel, result);
                if (childIndex < blob.Nodes.Length)
                    childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
        }
        
        private static void DetectNestedRepeater(
            BehaviorTreeBlob blob,
            int nodeIndex,
            bool insideRepeater,
            ValidationResult result)
        {
            if (nodeIndex >= blob.Nodes.Length) return;
            var node = blob.Nodes[nodeIndex];
            
            if (node.Type == NodeType.Repeater)
            {
                if (insideRepeater)
                {
                    result.Warnings.Add(
                        $"Node {nodeIndex}: Nested Repeater detected! " +
                        "Both Repeater nodes will conflict on LocalRegisters[0]. " +
                        "This will cause incorrect iteration counts. Consider restructuring the tree.");
                }
                insideRepeater = true;
            }
            
            // Recursively check children
            int childIndex = nodeIndex + 1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                DetectNestedRepeater(blob, childIndex, insideRepeater, result);
                if (childIndex < blob.Nodes.Length)
                    childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
        }
    }
}
