using System;
using System.Text;
using System.Globalization;
using Fbt.Serialization;

namespace Fbt.Utilities
{
    /// <summary>
    /// Generates text-based visualization of behavior tree structure.
    /// </summary>
    public static class TreeVisualizer
    {
        public static string Visualize(BehaviorTreeBlob blob)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tree: {blob.TreeName}");
            sb.AppendLine($"Nodes: {blob.Nodes.Length}, Methods: {blob.MethodNames.Length}");
            sb.AppendLine();
            
            VisualizeNode(blob, 0, 0, sb);
            return sb.ToString();
        }
        
        private static void VisualizeNode(BehaviorTreeBlob blob, int index, int depth, StringBuilder sb)
        {
            if (index >= blob.Nodes.Length)
                return;
                
            var node = blob.Nodes[index];
            string indent = new string(' ', depth * 2);
            
            // Node info
            sb.Append($"{indent}[{index}] {node.Type}");
            
            // Add method name for actions
            if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
            {
                if (node.PayloadIndex >= 0 && node.PayloadIndex < blob.MethodNames.Length)
                {
                    sb.Append($" \"{blob.MethodNames[node.PayloadIndex]}\"");
                }
            }
            
            // Add params for Wait/Repeater/Cooldown
            if (node.Type == NodeType.Wait && node.PayloadIndex >= 0)
            {
                if (node.PayloadIndex < blob.FloatParams.Length)
                    sb.Append($" ({blob.FloatParams[node.PayloadIndex].ToString(CultureInfo.InvariantCulture)}s)");
            }
            if (node.Type == NodeType.Cooldown && node.PayloadIndex >= 0)
            {
                 if (node.PayloadIndex < blob.FloatParams.Length)
                    sb.Append($" (Cooldown: {blob.FloatParams[node.PayloadIndex].ToString(CultureInfo.InvariantCulture)}s)");
            }
            if (node.Type == NodeType.Repeater && node.PayloadIndex >= 0)
            {
                 if (node.PayloadIndex < blob.IntParams.Length)
                    sb.Append($" (x{blob.IntParams[node.PayloadIndex]})");
            }
             if (node.Type == NodeType.Parallel && node.PayloadIndex >= 0)
            {
                 if (node.PayloadIndex < blob.IntParams.Length)
                 {
                    int policy = blob.IntParams[node.PayloadIndex];
                    string policyName = policy == 0 ? "RequireAll" : "RequireOne";
                    sb.Append($" ({policyName})");
                 }
            }
            
            sb.AppendLine($" | Children: {node.ChildCount}, Offset: {node.SubtreeOffset}");
            
            // Recursively visualize children
            int childIndex = index + 1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                VisualizeNode(blob, childIndex, depth + 1, sb);
                childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
        }
    }
}
