using System;
using Xunit;

namespace Bagira.DDS.DataModel.Tests
{
    /// <summary>
    /// Reflection-based contract tests that verify the DDS message field shapes
    /// introduced / removed by the ATTR-S1 sprint tasks.
    ///
    /// These tests do NOT require a live DDS participant — they operate purely on
    /// the compiled type metadata and therefore run in any CI environment.
    /// </summary>
    public class GenericMessageFieldTests
    {
        // ── ATTR-S1T1: CreateEntityRequest ────────────────────────────────────

        /// <summary>
        /// <see cref="Bagira.BDC.SSTM.CreateEntityRequest"/> must expose
        /// <c>InitialAttributesJson</c> (a nullable <see cref="string"/> field)
        /// so the SimHost can receive the serialised attribute JSON from the IG.
        /// </summary>
        [Fact]
        public void CreateEntityRequest_HasInitialAttributesJsonField()
        {
            var field = typeof(Bagira.BDC.SSTM.CreateEntityRequest).GetField("InitialAttributesJson");
            Assert.NotNull(field);
            Assert.Equal(typeof(string), field!.FieldType);
        }

        /// <summary>
        /// The legacy <c>InitialAttributes</c> field (a <c>List&lt;EntityAttributePayload&gt;</c>)
        /// must no longer exist on <see cref="Bagira.BDC.SSTM.CreateEntityRequest"/>.
        /// </summary>
        [Fact]
        public void CreateEntityRequest_HasNoInitialAttributesField()
        {
            var field = typeof(Bagira.BDC.SSTM.CreateEntityRequest).GetField("InitialAttributes");
            Assert.Null(field);
        }

        // ── ATTR-S1T2: UpdateEntityAttributeRequest ───────────────────────────

        /// <summary>
        /// <see cref="Bagira.BDC.SSTM.UpdateEntityAttributeRequest"/> must expose
        /// <c>AttributePatchJson</c> (a <see cref="string"/> field) in place of the
        /// old enum-keyed payload pair.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_HasAttributePatchJsonField()
        {
            var field = typeof(Bagira.BDC.SSTM.UpdateEntityAttributeRequest).GetField("AttributePatchJson");
            Assert.NotNull(field);
            Assert.Equal(typeof(string), field!.FieldType);
        }

        /// <summary>
        /// The legacy <c>AttributeId</c> field (an <c>EntityAttribute</c> enum value)
        /// must no longer exist on <see cref="Bagira.BDC.SSTM.UpdateEntityAttributeRequest"/>.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_HasNoAttributeIdField()
        {
            var field = typeof(Bagira.BDC.SSTM.UpdateEntityAttributeRequest).GetField("AttributeId");
            Assert.Null(field);
        }

        /// <summary>
        /// The legacy <c>Payload</c> field (an <c>EntityAttributePayload</c> union)
        /// must no longer exist on <see cref="Bagira.BDC.SSTM.UpdateEntityAttributeRequest"/>.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_HasNoPayloadField()
        {
            var field = typeof(Bagira.BDC.SSTM.UpdateEntityAttributeRequest).GetField("Payload");
            Assert.Null(field);
        }

        /// <summary>
        /// The <c>EntityAttribute</c> enum that used to live in the
        /// <c>Bagira.BDC.SSTM</c> namespace (assembly <c>Bagira.DDS.DataModel</c>)
        /// must have been deleted entirely.
        /// </summary>
        [Fact]
        public void GenericMessages_EntityAttribute_EnumDoesNotExist()
        {
            var type = Type.GetType("Bagira.BDC.SSTM.EntityAttribute, Bagira.DDS.DataModel");
            Assert.Null(type);
        }

        // ── TWOACK-DM001: DeleteEntityRequest struct ────────────────────────────

        [Fact]
        public void DeleteEntityRequest_HasRequestIdField()
        {
            var field = typeof(Bagira.BDC.SSTM.DeleteEntityRequest).GetField("RequestId");
            Assert.NotNull(field);
            Assert.Equal(typeof(Guid), field!.FieldType);
        }

        [Fact]
        public void DeleteEntityRequest_HasEntityIdField()
        {
            var field = typeof(Bagira.BDC.SSTM.DeleteEntityRequest).GetField("EntityId");
            Assert.NotNull(field);
            Assert.Equal(typeof(int), field!.FieldType);
        }

        // ── TWOACK-DM002: SstStatusCode enum ────────────────────────────────────

        [Fact]
        public void SstStatusCode_Success_IsZero()
        {
            Assert.Equal(0, (int)Bagira.BDC.SSTM.SstStatusCode.Success);
        }

        [Fact]
        public void SstStatusCode_InProgress_IsOne()
        {
            Assert.Equal(1, (int)Bagira.BDC.SSTM.SstStatusCode.InProgress);
        }

        [Fact]
        public void SstStatusCode_UnknownDescriptorType_IsTwo()
        {
            Assert.Equal(2, (int)Bagira.BDC.SSTM.SstStatusCode.UnknownDescriptorType);
        }

        [Fact]
        public void SstStatusCode_ErrorCodesAreAllAboveOne()
        {
            // All error condition codes (i.e. not Success and not InProgress)
            // must have a value >= 2 so that the check `statusCode >= 2` works.
            var codes = (Bagira.BDC.SSTM.SstStatusCode[])Enum.GetValues(
                typeof(Bagira.BDC.SSTM.SstStatusCode));
            foreach (var code in codes)
            {
                if (code == Bagira.BDC.SSTM.SstStatusCode.Success ||
                    code == Bagira.BDC.SSTM.SstStatusCode.InProgress)
                    continue;
                Assert.True((int)code >= 2,
                    $"SstStatusCode.{code} = {(int)code} should be >= 2.");
            }
        }

        // ── TWOACK-DM003: CreateUpdateDeleteEntityAck struct ───────────────────

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasEntityIdField()
        {
            var field = typeof(Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck).GetField("EntityId");
            Assert.NotNull(field);
            Assert.Equal(typeof(int), field!.FieldType);
        }

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasStatusCodeField()
        {
            var field = typeof(Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck).GetField("StatusCode");
            Assert.NotNull(field);
            Assert.Equal(typeof(int), field!.FieldType);
        }

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasNoErrorCodeField()
        {
            var field = typeof(Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck).GetField("ErrorCode");
            Assert.Null(field);
        }

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasNoNewEntityIdField()
        {
            var field = typeof(Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck).GetField("NewEntityId");
            Assert.Null(field);
        }
    }
}
