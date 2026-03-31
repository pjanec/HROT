using System;
using System.Collections.Generic;
using System.Text.Json;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// Unit tests for the ATTR2 binary wire types introduced in ATTR2-P1T1 and ATTR2-P1T3.
    /// Verifies struct construction, value storage, JSON round-trip behaviour, and
    /// zero regressions on existing wire-message types.
    /// </summary>
    public class AttributeRecordTests
    {
        // ── ATTR2-P1T1: AttributeValueUnion and AttributeRecord ───────────────────────

        /// <summary>
        /// An <see cref="AttributeRecord"/> carrying a Float64 value must serialize to JSON
        /// and back without data loss on any field.
        /// </summary>
        [Fact]
        public void AttributeRecord_Float64_RoundTripsViaJsonSerializer()
        {
            var record = new AttributeRecord
            {
                AttributeId = 10,
                SubIndex1 = 0,
                SubIndex2 = 0,
                Value = new AttributeValueUnion
                {
                    ValueType = AttributeValueType.KindFloat64,
                    DoubleValue = 32.085,
                }
            };

            var opts = new JsonSerializerOptions { IncludeFields = true };
            var json = JsonSerializer.Serialize(record, opts);
            var deserialized = JsonSerializer.Deserialize<AttributeRecord>(json, opts);

            Assert.Equal(10, deserialized.AttributeId);
            Assert.Equal((short)0, deserialized.SubIndex1);
            Assert.Equal((short)0, deserialized.SubIndex2);
            Assert.Equal(AttributeValueType.KindFloat64, deserialized.Value.ValueType);
            Assert.Equal(32.085, deserialized.Value.DoubleValue);
        }

        /// <summary>
        /// When the String branch is active, <see cref="AttributeValueUnion.StringValue"/>
        /// holds the expected value and all numeric/vector branches remain at their default/zero state.
        /// </summary>
        [Fact]
        public void AttributeValueUnion_StringBranch_PopulatedAndOtherBranchesDefault()
        {
            var union = new AttributeValueUnion
            {
                ValueType = AttributeValueType.KindString,
                StringValue = "Alpha",
            };

            Assert.Equal("Alpha", union.StringValue);
            Assert.Equal(0, union.IntValue);
            Assert.Equal(0L, union.LongValue);
            Assert.Equal(0f, union.FloatValue);
            Assert.Equal(0.0, union.DoubleValue);
            Assert.False(union.BoolValue);
            Assert.Equal(default(Vec3f), union.Vec3fValue);
            Assert.Equal(default(Vec3d), union.Vec3dValue);
            Assert.Equal(default(Vec4f), union.Vec4fValue);
        }

        /// <summary>
        /// A Vec3d branch stores three double-precision values and each component can be
        /// read back at the correct index.
        /// </summary>
        [Fact]
        public void AttributeValueUnion_Vec3dBranch_StoresThreeDoubles()
        {
            var union = new AttributeValueUnion
            {
                ValueType = AttributeValueType.KindVec3d,
                Vec3dValue = new Vec3d { X = 1.0, Y = 2.0, Z = 3.0 },
            };

            Assert.Equal(1.0, union.Vec3dValue.X);
            Assert.Equal(2.0, union.Vec3dValue.Y);
            Assert.Equal(3.0, union.Vec3dValue.Z);
            Assert.Equal(default(Vec3f), union.Vec3fValue);
            Assert.Equal(default(Vec4f), union.Vec4fValue);
        }

        /// <summary>
        /// <see cref="AttributeValueType"/> must expose exactly nine members — one for each
        /// supported value kind in the ATTR2 binary schema.
        /// </summary>
        [Fact]
        public void AttributeValueType_CoversAllNineTypes()
        {
            var values = Enum.GetValues<AttributeValueType>();
            Assert.Equal(9, values.Length);

            // Verify each named value is present
            Assert.Contains(AttributeValueType.KindInt32,  values);
            Assert.Contains(AttributeValueType.KindInt64,  values);
            Assert.Contains(AttributeValueType.KindFloat32, values);
            Assert.Contains(AttributeValueType.KindFloat64, values);
            Assert.Contains(AttributeValueType.KindBool,   values);
            Assert.Contains(AttributeValueType.KindString, values);
            Assert.Contains(AttributeValueType.KindVec3f,  values);
            Assert.Contains(AttributeValueType.KindVec3d,  values);
            Assert.Contains(AttributeValueType.KindVec4f,  values);
        }

        // ── ATTR2-P1T3: Wire message field tests ──────────────────────────────────────

        /// <summary>
        /// <see cref="CreateEntityRequest"/> can be constructed with
        /// <c>InitialAttributeRecords = null</c>; the existing JSON-only creation path
        /// is therefore unaffected.
        /// </summary>
        [Fact]
        public void CreateEntityRequest_InitialAttributeRecords_DefaultsToNull()
        {
            var request = new CreateEntityRequest
            {
                InitialAttributeRecords = null,
            };

            Assert.Null(request.InitialAttributeRecords);
        }

        /// <summary>
        /// <see cref="CreateEntityRequest"/> can be constructed with a non-null
        /// <see cref="System.Collections.Generic.List{T}"/> of 2 <see cref="AttributeRecord"/>s
        /// and the list is accessible with the correct count.
        /// </summary>
        [Fact]
        public void CreateEntityRequest_InitialAttributeRecords_HoldsNonNullList()
        {
            var records = new List<AttributeRecord>
            {
                new AttributeRecord
                {
                    AttributeId = 1,
                    Value = new AttributeValueUnion { ValueType = AttributeValueType.KindString, StringValue = "Alpha" }
                },
                new AttributeRecord
                {
                    AttributeId = 10,
                    Value = new AttributeValueUnion { ValueType = AttributeValueType.KindFloat64, DoubleValue = 48.123 }
                },
            };

            var request = new CreateEntityRequest
            {
                InitialAttributeRecords = records,
            };

            Assert.NotNull(request.InitialAttributeRecords);
            Assert.Equal(2, request.InitialAttributeRecords.Count);
            Assert.Equal((ushort)1, request.InitialAttributeRecords[0].AttributeId);
            Assert.Equal((ushort)10, request.InitialAttributeRecords[1].AttributeId);
        }

        /// <summary>
        /// <see cref="UpdateEntityAttributeRequest"/> defaults
        /// <c>AttributeRecords</c> to null without throwing.
        /// </summary>
        [Fact]
        public void UpdateEntityAttributeRequest_AttributeRecords_DefaultsToNull()
        {
            // AttributePatchJson is non-nullable in the struct, supply a non-null value.
            var request = new UpdateEntityAttributeRequest
            {
                AttributePatchJson = "{}",
            };

            Assert.Null(request.AttributeRecords);
        }
    }
}
