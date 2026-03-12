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
    }
}
