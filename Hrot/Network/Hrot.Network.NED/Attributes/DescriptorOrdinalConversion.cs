using System;
using Fdp.Toolkit.Replication;
using Hrot.NED.Descriptors;

namespace Hrot.SimHost.Installers;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-017</c> — the declared boundary between the FDP-side and network-side descriptor-ordinal
/// vocabularies.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §16. 🔒 User ruling: *"same numeric value,
/// translated in network translator, accepted cost for network agnosticism."*</para>
///
/// <para>⭐⭐ <b>Sibling of <see cref="AttributeRecordConversion"/>, and deliberately shaped the same way</b>
/// — the pair <see cref="Patching.AttributeValueKind"/>/<c>AttributeValueType</c> is already translated there
/// under <c>R-134</c>. ⇒ ⭐ one place to look for *"where do the two vocabularies meet?"*, per descriptor and
/// per value kind.</para>
///
/// <para>⭐⭐⭐ <b>Why a CAST is not used, even though the numbers are identical.</b> They are identical
/// *today*, and <c>TheDescriptorOrdinalVocabulariesAgreeTests</c> keeps them so. ⛔ A cast would silently
/// follow whichever enum moved, which is precisely the failure the duplication exists to make impossible.
/// ⭐ Checked conversion turns a renumber into a thrown exception at the boundary and a RED in the rail —
/// ⛔ never a wrong bit index quietly indexing the wrong descriptor.</para>
///
/// <para>⚠ <b>Named a "conversion", but at runtime it is a VALIDATED pass-through</b> — 📌 stated so nobody
/// reads a mapping table into it that is not there. The value is the CHECK, not a remap.</para>
/// </summary>
public static class DescriptorOrdinalConversion
{
    /// <summary>⭐ FDP-side ordinal → the network vocabulary.</summary>
    public static EDescriptorType ToNetwork(DescriptorOrdinal ordinal)
    {
        var candidate = (EDescriptorType)(long)ordinal;

        if (!Enum.IsDefined(typeof(EDescriptorType), candidate))
            throw new ArgumentOutOfRangeException(
                nameof(ordinal), ordinal,
                $"DescriptorOrdinal.{ordinal} has no EDescriptorType with the same value. The two " +
                "vocabularies have diverged — add the member on the network side rather than casting.");

        return candidate;
    }

    /// <summary>⭐ The network vocabulary → the FDP-side ordinal.</summary>
    public static DescriptorOrdinal ToInternal(EDescriptorType type)
    {
        var candidate = (DescriptorOrdinal)(long)type;

        if (!Enum.IsDefined(typeof(DescriptorOrdinal), candidate))
            throw new ArgumentOutOfRangeException(
                nameof(type), type,
                $"EDescriptorType.{type} has no DescriptorOrdinal with the same value. The two " +
                "vocabularies have diverged — add the member on the FDP side rather than casting.");

        return candidate;
    }
}
