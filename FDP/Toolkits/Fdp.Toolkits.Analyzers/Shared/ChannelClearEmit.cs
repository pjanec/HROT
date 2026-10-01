using System.Collections.Generic;
using System.Text;

namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐ <c>CE-417</c> — THE one emitter for "a <c>[WritesChannel]</c> action that returns <c>Failure</c> releases its channels".
    /// <para>Linked into BOTH <c>Fdp.Toolkits.Analyzers</c> (<c>BTreeActionGenerator</c>'s 4-param wrapper) and
    /// <c>Hrot.AiEditor.Persistence</c> (the BTree asset's per-binding <c>[SharedAi*]</c> call, which replaced the analyzer's
    /// per-method adapter) — the same pattern as <c>HsmActionKey</c>: a linked file crosses the netstandard/net8 wall an
    /// assembly reference cannot, so the channel-kind mapping exists once.</para>
    /// </summary>
    internal static class ChannelClearEmit
    {
        public const string LocomotionChannelType  = "global::Fdp.Toolkit.Behavior.Components.LocomotionChannel";
        public const string WeaponChannelType      = "global::Fdp.Toolkit.Behavior.Components.WeaponChannel";
        public const string InteractionChannelType = "global::Fdp.Toolkit.Behavior.Components.InteractionChannel";

        public static string? ChannelKindToType(int kind) => kind switch
        {
            0 => LocomotionChannelType,
            1 => WeaponChannelType,
            2 => InteractionChannelType,
            _ => null,
        };

        /// <summary>Emits the release block, reading a local <c>status</c> and the BTree context <c>ctx</c>.</summary>
        public static void Emit(StringBuilder sb, IReadOnlyList<int> channels, string indent)
        {
            sb.AppendLine(indent + "if (status == global::Fbt.NodeStatus.Failure)");
            sb.AppendLine(indent + "{");
            foreach (int kind in channels)
            {
                string? ct = ChannelKindToType(kind);
                if (ct == null) continue;
                sb.AppendLine(indent + "    ref var ch" + kind + " = ref ctx.World.GetComponentRW<" + ct + ">(ctx.Self);");
                sb.AppendLine(indent + "    ch" + kind + ".ActiveAction     = 0;");
                sb.AppendLine(indent + "    ch" + kind + ".ActionInstanceId = unchecked(ch" + kind + ".ActionInstanceId + 1u);");
            }
            sb.AppendLine(indent + "}");
        }
    }
}
