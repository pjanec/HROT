using System.Collections.Generic;
using System.Text;

namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐ <c>CE-417</c> — THE one emitter for "a <c>[WritesChannel]</c> action that returns <c>Failure</c> releases its channels" — since
    /// <c>CE-1411</c> a call to the runtime <c>ChannelRelease.OnFailure</c>, which a C#-built tree's binder calls too.
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

        /// <summary>
        /// Emits the release, reading a local <c>status</c> and the BTree context <c>ctx</c>: one call per channel to
        /// <c>Fdp.Toolkit.Behavior.ChannelRelease.OnFailure</c> — ⭐ <c>CE-1411</c>: the ONE runtime implementation, which a C#-built
        /// tree's <c>SharedNodeBinder</c> thunk calls too. ⛔ HISTORY: the two channel writes were emitted inline here, and the C# path had
        /// no release at all.
        /// </summary>
        public static void Emit(StringBuilder sb, IReadOnlyList<int> channels, string indent)
        {
            foreach (int kind in channels)
            {
                if (ChannelKindToType(kind) == null) continue;
                sb.AppendLine(indent + "global::Fdp.Toolkit.Behavior.ChannelRelease.OnFailure(status, ctx.World, ctx.Self, (global::Fbt.Kernel.ChannelKind)" + kind + ");");
            }
        }
    }
}
