using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — a registered BTree behaviour as CANONICAL TEXT: what a run of it does, written so two registrations that
    /// behave the same print the same and any difference that matters prints differently. Read off the PRODUCTION definition (its interpreter's
    /// blob and its parse), never off a source file:
    /// <list type="bullet">
    /// <item>the definition facts the pickers and the ingress read — group, "is an SOP" (CE-1019), its channel set, its authored contract;</item>
    /// <item>the parse: what each probe payload does (ok / refused), and for "{}" the VALUE every bound node reads;</item>
    /// <item>the tree, depth first — every node's kind, editor id, title and comment, a decorator's / wait's parameter, a leaf's bound METHOD,
    ///   the TYPE and VALUE of the params it reads and the TYPE of its working memory, an SOP order's behaviour and urgency.</item>
    /// </list>
    /// ⚠ NOT printed, on purpose: byte offsets and the stateful slot keys — a JSON asset's working memory lived in per-node occurrence slots
    /// (<c>@po@slotHash</c>), a C#-built tree's in its block (<c>@po@offset</c>, the CombatPosture port's form, R-266); both are one memory per
    /// node per run, held across a pause (CE-2081), so the storage address is not behaviour.
    /// </summary>
    internal static class ShippedTreeDump
    {
        /// <summary>The payloads each tree's parse is probed with; the last is per tree (its contract's own fields).</summary>
        private const string SopPrefix = "Fdp.Toolkit.Behavior.SopActions.";

        public static readonly string[] CommonProbes = { "", "{}", "{\"bogus\":1}" };

        public static string Of(BehaviorRegistry registry, string name, string assetId, IReadOnlyList<string> extraProbes,
            IReadOnlyDictionary<string, (string Label, string? Comment)>? titles = null)
        {
            if (!registry.TryGetId(name, out int id) || !registry.TryGetDefinition(id, out var def) || def == null)
                throw new InvalidOperationException($"{name} is not registered");
            var blob = def.BTreeInterpreter?.Blob ?? throw new InvalidOperationException($"{name} is not a BTree");
            var sb = new StringBuilder();
            sb.Append("asset ").AppendLine(assetId);
            sb.Append("group ").AppendLine(def.Group ?? "-");
            sb.Append("sop-orders ").AppendLine(def.CarriesSopOrders ? "yes" : "no");
            sb.Append("writes-channels ").AppendLine(def.WritesChannels == null ? "unknown" : "[" + string.Join(",", def.WritesChannels.Select(t => t.Name)) + "]");
            sb.Append("json-params ").AppendLine(def.JsonParamsDtoType?.FullName ?? "-");

            var leaves = Leaves(blob);
            foreach (var probe in CommonProbes.Concat(extraProbes))
            {
                var (ok, block) = Parse(def, probe);
                sb.Append("parse ").Append(probe.Length == 0 ? "(empty)" : probe).Append(" -> ").AppendLine(ok);
                if (block != null && probe != "{}")
                    foreach (var l in leaves.Where(l => l.Param != null))
                        sb.Append("  ").Append(l.VisualId).Append(' ').AppendLine(Value(l.Param!, l.Offset, block));
            }
            var (_, bake) = Parse(def, "{}");
            Tree(blob, 0, 0, sb, titles, bake);
            return sb.ToString().Replace("\r\n", "\n");
        }

        private sealed record Leaf(string VisualId, Type? Param, int Offset);

        private static List<Leaf> Leaves(BehaviorTreeBlob blob)
        {
            var list = new List<Leaf>();
            for (int i = 0; i < blob.Nodes.Length; i++)
            {
                var n = blob.Nodes[i];
                if (n.Type != NodeType.Action && n.Type != NodeType.Condition) continue;
                if (blob.MethodNames[n.PayloadIndex].StartsWith(SopPrefix, StringComparison.Ordinal)) continue;   // an order binds no params
                var b = Bind(blob.MethodNames[n.PayloadIndex]);
                list.Add(new Leaf(blob.DebugMetadata![i].VisualId, b.Param, b.Offset));
            }
            return list;
        }

        private static unsafe (string Outcome, byte[]? Block) Parse(BehaviorDefinition def, string json)
        {
            int bytes = RootParamsAccess.RootParamsBytes(def);
            var block = new byte[Math.Max(bytes, 1)];
            if (def.ParseParams == null)
            {
                if (def.BakeDefaults != null) fixed (byte* p = block) def.BakeDefaults(p, block.Length);
                return (def.BakeDefaults != null ? "baked (no parse)" : "no params region", block);
            }
            try
            {
                using var world = new EntityRepository();
                fixed (byte* p = block) def.ParseParams(json, p, bytes, world, default);
                return ("ok", block);
            }
            catch (Exception ex) { return ("refused " + ex.GetType().Name, null); }
        }

        private static int Tree(BehaviorTreeBlob blob, int index, int depth, StringBuilder sb,
            IReadOnlyDictionary<string, (string Label, string? Comment)>? titles, byte[]? bake)
        {
            var n = blob.Nodes[index];
            var meta = blob.DebugMetadata![index];
            string label = titles != null && titles.TryGetValue(meta.VisualId, out var t) && t.Label != null ? t.Label : meta.Label;
            string? comment = titles != null && titles.TryGetValue(meta.VisualId, out var tc) ? tc.Comment : meta.CustomComment;
            string pad = new string(' ', depth * 2);
            sb.Append(pad).Append(n.Type).Append(' ').Append(meta.VisualId);
            switch (n.Type)
            {
                case NodeType.Action:
                case NodeType.Condition:
                    sb.Append(' ').Append(Describe(blob.MethodNames[n.PayloadIndex], bake));
                    label = IsKeyLabel(label, blob.MethodNames[n.PayloadIndex]) ? "" : label;   // a builder default, not a title
                    break;
                case NodeType.Wait: case NodeType.Cooldown:
                    sb.Append(" seconds=").Append(blob.FloatParams[n.PayloadIndex].ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case NodeType.Repeater: case NodeType.Parallel:
                    sb.Append(" n=").Append(blob.IntParams[n.PayloadIndex]);
                    break;
                case NodeType.Subtree:
                    sb.Append(" tree=").Append(blob.SubtreeAssetIds[n.PayloadIndex]);
                    break;
            }
            if (!string.IsNullOrEmpty(label) && !IsDefaultLabel(n.Type, label)) sb.Append(" \"").Append(label).Append('"');
            sb.AppendLine();
            if (!string.IsNullOrEmpty(comment)) sb.Append(pad).Append("  # ").AppendLine(comment);
            int child = index + 1;
            for (int c = 0; c < n.ChildCount; c++)
            {
                Tree(blob, child, depth + 1, sb, titles, bake);
                child += blob.Nodes[child].SubtreeOffset;
            }
            return index;
        }

        /// <summary>A label the builder wrote itself (the node kind, a decorator's "Repeater(-1x)", a leaf's key or method name).</summary>
        private static bool IsDefaultLabel(NodeType type, string label)
            => label == type.ToString() || label.StartsWith(type + "(", StringComparison.Ordinal);

        private static bool IsKeyLabel(string label, string key)
            => label == key || label == key.Split('@')[0].Split('.').Last();

        private static string Describe(string key, byte[]? bake)
        {
            if (key.StartsWith(SopPrefix, StringComparison.Ordinal))
                return "sop " + key.Substring(SopPrefix.Length).Split('@')[0];
            var b = Bind(key);
            var s = new StringBuilder("call ").Append(b.Method);
            if (b.Param != null) s.Append(" p:").Append(b.Param.Name).Append('=').Append(bake == null ? "?" : Value(b.Param, b.Offset, bake));
            if (b.State != null) s.Append(" state:").Append(b.State.Name);
            return s.ToString();
        }

        private static (string Method, Type? Param, int Offset, Type? State) Bind(string key)
        {
            var parts = key.Split('@');
            string fqn = parts[0];
            int dot = fqn.LastIndexOf('.');
            string typeName = fqn.Substring(0, dot), methodName = fqn.Substring(dot + 1);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(x => x != null)
                ?? throw new InvalidOperationException($"no type {typeName} for key {key}");
            int arity = parts.Length switch { 1 => 2, 2 => 3, _ => 4 };
            var m = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == methodName && x.GetParameters().Length == arity
                                     && (arity == 2 || x.GetParameters()[0].ParameterType.IsByRef))
                ?? throw new InvalidOperationException($"no method {methodName}/{arity} on {typeName} for key {key}");
            var ps = m.GetParameters();
            Type? param = arity >= 3 ? ps[0].ParameterType.GetElementType() : null;
            Type? state = arity == 4 ? ps[1].ParameterType.GetElementType() : null;
            int offset = arity >= 3 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : -1;
            return (typeName + "." + methodName, param, offset, state);
        }

        private static string Value(Type t, int offset, byte[] block)
        {
            var read = typeof(ShippedTreeDump).GetMethod(nameof(ReadAt), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(t);
            object v = read.Invoke(null, new object[] { block, offset })!;
            return JsonSerializer.Serialize(v, t, BehaviorParams.JsonOptions);
        }

        private static object ReadAt<T>(byte[] block, int offset) where T : struct
            => MemoryMarshal.Read<T>(block.AsSpan(offset));

        /// <summary>The repo root (the folder holding HROT.sln), found from the test binary.</summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HROT.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("HROT.sln not found above " + AppContext.BaseDirectory);
        }

        public static string SnapshotPath(string name)
            => Path.Combine(RepoRoot(), "Hrot", "Subsystems", "Hrot.SimHost.Tests", "Snapshots", "ShippedTrees", name + ".tree.txt");
    }
}
