namespace Fdp.Toolkit.Replication;

/// <summary>
/// ⭐⭐ <b><c>AX-017</c> — the FDP-side force-affiliation encoding, for decoding external payloads.</b>
///
/// <para>🔒 Same user ruling as <see cref="DescriptorOrdinal"/>: *"same numeric value, translated in network
/// translator, accepted cost for network agnosticism."*</para>
///
/// <para>⭐ <b>Why FDP code needs it at all.</b> <c>AttributeCompilerFactory.Build</c>'s <c>"Affiliation"</c>
/// path decodes a JSON INTEGER — ExCon's default enum serialisation emits <c>2</c> rather than
/// <c>"FORCE_OPPOSING"</c> — into <see cref="Fdp.Core.ForceId"/>. ⇒ the apply path must know the external
/// numbering, and ⛔ it must not reach into the network assembly to learn it.</para>
///
/// <para>⚠⚠ <b>THIS MAKES THREE COPIES, and that is worth saying out loud.</b> 📐 Measured
/// <c>2026-08-26</c>: <c>Hrot.NED.Descriptors.eForceIdentifier</c> *(DDS)* and
/// <c>Hrot.Core.Mission.eForceIdentifier</c> already both exist, agree on <c>0,1,2,3</c>, and are kept in
/// step by a COMMENT with no rail. ⭐ This copy is at least railed
/// *(`TheDescriptorOrdinalVocabulariesAgreeTests`)*. ⛔ Consolidating the two pre-existing Hrot copies is
/// NOT in this slice's scope — filed rather than silently widened.</para>
/// </summary>
public enum ForceIdentifier
{
    Unknown  = 0,
    Friendly = 1,
    Opposing = 2,
    Neutral  = 3,
}
