using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Tests.Serialization.Migrations.Internal
{
    public class DomDifferTests
    {
        // T1-220: Identical objects return null
        [Fact]
        public void T1_220_IdenticalObjects_ReturnsNull()
        {
            var a = JsonNode.Parse("{\"x\":1}")!.AsObject();
            var b = JsonNode.Parse("{\"x\":1}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.Null(result);
        }

        // T1-221: Field present in B but not in A -> OldValue="null", NewValue=serialized
        [Fact]
        public void T1_221_NewFieldInB_ReportedAsModified()
        {
            var a = JsonNode.Parse("{\"x\":1}")!.AsObject();
            var b = JsonNode.Parse("{\"x\":1,\"y\":2}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            Assert.True(rootObj.IsModified);

            var y = rootObj.Children.Find(c => c.Name == "y");
            Assert.NotNull(y);
            Assert.True(y!.IsModified);
            var yVal = Assert.IsType<DiffValue>(y);
            Assert.Equal("null", yVal.OldValue);
            Assert.Equal("2", yVal.NewValue);
        }

        // T1-222: Field present in A but missing in B -> OldValue=serialized, NewValue="null"
        [Fact]
        public void T1_222_FieldMissingInB_ReportedAsModified()
        {
            var a = JsonNode.Parse("{\"x\":1,\"y\":2}")!.AsObject();
            var b = JsonNode.Parse("{\"x\":1}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);

            var y = rootObj.Children.Find(c => c.Name == "y");
            Assert.NotNull(y);
            Assert.True(y!.IsModified);
            var yVal = Assert.IsType<DiffValue>(y);
            Assert.Equal("2", yVal.OldValue);
            Assert.Equal("null", yVal.NewValue);
        }

        // T1-223: Same field, different value -> DiffValue IsModified, correct OldValue/NewValue
        [Fact]
        public void T1_223_ChangedField_ReportsOldAndNewValues()
        {
            var a = JsonNode.Parse("{\"x\":1}")!.AsObject();
            var b = JsonNode.Parse("{\"x\":2}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            var x = rootObj.Children.Find(c => c.Name == "x");
            Assert.NotNull(x);
            var xVal = Assert.IsType<DiffValue>(x);
            Assert.True(xVal.IsModified);
            Assert.Equal("1", xVal.OldValue);
            Assert.Equal("2", xVal.NewValue);
        }

        // T1-224: Nested difference -> outer and inner DiffObjects both IsModified
        [Fact]
        public void T1_224_NestedDifference_PropagatesIsModified()
        {
            var a = JsonNode.Parse("{\"outer\":{\"inner\":1}}")!.AsObject();
            var b = JsonNode.Parse("{\"outer\":{\"inner\":2}}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            Assert.True(rootObj.IsModified);

            var outer = rootObj.Children.Find(c => c.Name == "outer");
            Assert.NotNull(outer);
            var outerObj = Assert.IsType<DiffObject>(outer);
            Assert.True(outerObj.IsModified);

            var inner = outerObj.Children.Find(c => c.Name == "inner");
            Assert.NotNull(inner);
            Assert.True(inner!.IsModified);
        }

        // T1-225: Array element added -> DiffValue with ValueType=Array, IsModified
        [Fact]
        public void T1_225_ArrayElementAdded_IsModified()
        {
            var a = JsonNode.Parse("{\"arr\":[1,2]}")!.AsObject();
            var b = JsonNode.Parse("{\"arr\":[1,2,3]}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            var arr = rootObj.Children.Find(c => c.Name == "arr");
            Assert.NotNull(arr);
            var arrVal = Assert.IsType<DiffValue>(arr);
            Assert.True(arrVal.IsModified);
            Assert.Equal(JsonValueKind.Array, arrVal.ValueType);
        }

        // T1-226: Array element removed -> DiffValue IsModified
        [Fact]
        public void T1_226_ArrayElementRemoved_IsModified()
        {
            var a = JsonNode.Parse("{\"arr\":[1,2,3]}")!.AsObject();
            var b = JsonNode.Parse("{\"arr\":[1,2]}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            var arr = rootObj.Children.Find(c => c.Name == "arr");
            Assert.NotNull(arr);
            var arrVal = Assert.IsType<DiffValue>(arr);
            Assert.True(arrVal.IsModified);
        }

        // T1-227: Array element changed -> DiffValue IsModified
        [Fact]
        public void T1_227_ArrayElementChanged_IsModified()
        {
            var a = JsonNode.Parse("{\"arr\":[1,2,3]}")!.AsObject();
            var b = JsonNode.Parse("{\"arr\":[1,99,3]}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            var arr = rootObj.Children.Find(c => c.Name == "arr");
            Assert.NotNull(arr);
            var arrVal = Assert.IsType<DiffValue>(arr);
            Assert.True(arrVal.IsModified);
        }

        // T1-228: Type changed at same path (string -> number) -> IsModified
        [Fact]
        public void T1_228_TypeChangedAtPath_IsModified()
        {
            var a = JsonNode.Parse("{\"x\":\"foo\"}")!.AsObject();
            var b = JsonNode.Parse("{\"x\":42}")!.AsObject();

            var result = DomDiffer.Diff(a, b, "root");

            Assert.NotNull(result);
            var rootObj = Assert.IsType<DiffObject>(result);
            var x = rootObj.Children.Find(c => c.Name == "x");
            Assert.NotNull(x);
            Assert.True(x!.IsModified);
        }

        // T1-229: 50+ nested levels, identical content -> null (no stack overflow)
        [Fact]
        public void T1_229_DeeplyNested_IdenticalContent_NoStackOverflow_ReturnsNull()
        {
            var a = new JsonObject();
            var b = new JsonObject();
            var curA = a;
            var curB = b;

            for (int i = 0; i < 50; i++)
            {
                var childA = new JsonObject();
                var childB = new JsonObject();
                curA["nested"] = childA;
                curB["nested"] = childB;
                curA = childA;
                curB = childB;
            }

            curA["leaf"] = JsonValue.Create("same");
            curB["leaf"] = JsonValue.Create("same");

            var result = DomDiffer.Diff(a, b, "root");

            Assert.Null(result);
        }
    }
}
