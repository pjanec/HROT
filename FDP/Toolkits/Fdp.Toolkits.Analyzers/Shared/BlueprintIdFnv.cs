using System;

namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-2036</c> — the ONE asset-id hash: FNV-1a-32 over an asset GUID's 16 bytes.</b>
    /// 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8f" · <c>EQS_Design_v1.3_final.md</c> §17.4.
    ///
    /// <para>
    /// It is the id a blueprint registers under, the id the blueprint compiler bakes into a <c>SpawnEqsSensor</c>, the id
    /// <c>EqsTemplateRegistry</c> keys a template by, and the id an <c>[EqsTemplate]</c> registrar stages. ⛔ It was written
    /// four times (Fdp <c>BlueprintIdHash</c>, the compiler's <c>BlueprintIdHash</c>, <c>BlueprintClassNaming</c>,
    /// <c>BlueprintSignatureParser</c>) and a fifth producer — <c>EqsTemplateGenerator</c> — hashed the GUID's TEXT instead,
    /// so the template it staged sat under an id nothing looked up (<c>CE-2034</c>).
    /// </para>
    ///
    /// <para>
    /// ⛔ A linked, <c>internal</c> file rather than a public type for the reason <see cref="HsmActionKey"/> gives: the
    /// analyzer, the compiler's netstandard leg and <c>Hrot.AiEditor.Persistence</c> are netstandard2.0 and cannot reach a
    /// net8.0 public type. The two public <c>BlueprintIdHash.Compute</c> methods stay as façades over this one.
    /// </para>
    /// </summary>
    internal static class BlueprintIdFnv
    {
        private const uint OffsetBasis = 2166136261u;
        private const uint FnvPrime    = 16777619u;

        /// <summary>FNV-1a-32 over <paramref name="assetId"/>'s 16 bytes (<see cref="Guid.ToByteArray"/> order).</summary>
        public static int Compute(Guid assetId)
        {
            byte[] bytes = assetId.ToByteArray();
            unchecked
            {
                uint hash = OffsetBasis;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return (int)hash;
            }
        }
    }
}
