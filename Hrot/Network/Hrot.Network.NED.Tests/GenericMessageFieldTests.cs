using System;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
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
        /// <see cref="Hrot.NED.Messages.CreateEntityRequest"/> must expose
        /// <c>InitialAttributesJson</c> (a nullable <see cref="string"/> field)
        /// so the SimHost can receive the serialised attribute JSON from the IG.
        /// </summary>
        [Fact]
        public void CreateEntityRequest_HasInitialAttributesJsonField()
        {
            var field = typeof(Hrot.NED.Messages.CreateEntityRequest).GetField("InitialAttributesJson");
            Assert.NotNull(field);
            Assert.Equal(typeof(string), field!.FieldType);
        }

        /// <summary>
        /// The legacy <c>InitialAttributes</c> field (a <c>List&lt;EntityAttributePayload&gt;</c>)
        /// must no longer exist on <see cref="Hrot.NED.Messages.CreateEntityRequest"/>.
        /// </summary>
        [Fact]
        public void CreateEntityRequest_HasNoInitialAttributesField()
        {
            var field = typeof(Hrot.NED.Messages.CreateEntityRequest).GetField("InitialAttributes");
            Assert.Null(field);
        }

        // ── ATTR-S1T2: UpdateEntityAttributeRequest ───────────────────────────

        /// <summary>
        /// <see cref="Hrot.NED.Messages.UpdateEntityAttributeRequest"/> must expose
        /// <c>AttributePatchJson</c> (a <see cref="string"/> field) in place of the
        /// old enum-keyed payload pair.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_HasAttributePatchJsonField()
        {
            var field = typeof(Hrot.NED.Messages.UpdateEntityAttributeRequest).GetField("AttributePatchJson");
            Assert.NotNull(field);
            Assert.Equal(typeof(string), field!.FieldType);
        }

        /// <summary>
        /// The legacy <c>AttributeId</c> field (an <c>EntityAttribute</c> enum value)
        /// must no longer exist on <see cref="Hrot.NED.Messages.UpdateEntityAttributeRequest"/>.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_HasNoAttributeIdField()
        {
            var field = typeof(Hrot.NED.Messages.UpdateEntityAttributeRequest).GetField("AttributeId");
            Assert.Null(field);
        }

        /// <summary>
        /// The legacy <c>Payload</c> field (an <c>EntityAttributePayload</c> union)
        /// must no longer exist on <see cref="Hrot.NED.Messages.UpdateEntityAttributeRequest"/>.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_HasNoPayloadField()
        {
            var field = typeof(Hrot.NED.Messages.UpdateEntityAttributeRequest).GetField("Payload");
            Assert.Null(field);
        }

        /// <summary>
        /// The <c>EntityAttribute</c> enum that used to live in the
        /// <c>Hrot.NED.Messages</c> namespace (assembly <c>Hrot.NED</c>)
        /// must have been deleted entirely.
        /// </summary>
        [Fact]
        public void GenericMessages_EntityAttribute_EnumDoesNotExist()
        {
            var type = Type.GetType("Hrot.NED.Messages.EntityAttribute, Hrot.NED");
            Assert.Null(type);
        }

        // ── TWOACK-DM001: DeleteEntityRequest struct ────────────────────────────

        [Fact]
        public void DeleteEntityRequest_HasRequestIdField()
        {
            var field = typeof(Hrot.NED.Messages.DeleteEntityRequest).GetField("RequestId");
            Assert.NotNull(field);
            Assert.Equal(typeof(Guid), field!.FieldType);
        }

        [Fact]
        public void DeleteEntityRequest_HasEntityIdField()
        {
            var field = typeof(Hrot.NED.Messages.DeleteEntityRequest).GetField("EntityId");
            Assert.NotNull(field);
            Assert.Equal(typeof(int), field!.FieldType);
        }

        // ── TWOACK-DM002: NedStatusCode enum ────────────────────────────────────

        [Fact]
        public void NedStatusCode_Success_IsZero()
        {
            Assert.Equal(0, (int)Hrot.NED.Messages.NedStatusCode.Success);
        }

        [Fact]
        public void NedStatusCode_InProgress_IsOne()
        {
            Assert.Equal(1, (int)Hrot.NED.Messages.NedStatusCode.InProgress);
        }

        [Fact]
        public void NedStatusCode_UnknownDescriptorType_IsTwo()
        {
            Assert.Equal(2, (int)Hrot.NED.Messages.NedStatusCode.UnknownDescriptorType);
        }

        [Fact]
        public void NedStatusCode_ErrorCodesAreAllAboveOne()
        {
            // All error condition codes (i.e. not Success and not InProgress)
            // must have a value >= 2 so that the check `statusCode >= 2` works.
            var codes = (Hrot.NED.Messages.NedStatusCode[])Enum.GetValues(
                typeof(Hrot.NED.Messages.NedStatusCode));
            foreach (var code in codes)
            {
                if (code == Hrot.NED.Messages.NedStatusCode.Success ||
                    code == Hrot.NED.Messages.NedStatusCode.InProgress)
                    continue;
                Assert.True((int)code >= 2,
                    $"NedStatusCode.{code} = {(int)code} should be >= 2.");
            }
        }

        // ── TWOACK-DM003: CreateUpdateDeleteEntityAck struct ───────────────────

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasEntityIdField()
        {
            var field = typeof(Hrot.NED.Messages.CreateUpdateDeleteEntityAck).GetField("EntityId");
            Assert.NotNull(field);
            Assert.Equal(typeof(int), field!.FieldType);
        }

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasStatusCodeField()
        {
            var field = typeof(Hrot.NED.Messages.CreateUpdateDeleteEntityAck).GetField("StatusCode");
            Assert.NotNull(field);
            Assert.Equal(typeof(int), field!.FieldType);
        }

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasNoErrorCodeField()
        {
            var field = typeof(Hrot.NED.Messages.CreateUpdateDeleteEntityAck).GetField("ErrorCode");
            Assert.Null(field);
        }

        [Fact]
        public void CreateUpdateDeleteEntityAck_HasNoNewEntityIdField()
        {
            var field = typeof(Hrot.NED.Messages.CreateUpdateDeleteEntityAck).GetField("NewEntityId");
            Assert.Null(field);
        }
    }
}
