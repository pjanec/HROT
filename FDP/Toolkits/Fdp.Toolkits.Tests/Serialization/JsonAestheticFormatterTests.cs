using Fdp.Toolkit.Serialization;
using Xunit;

namespace Fdp.Toolkit.Serialization.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JsonAestheticFormatter.FlattenNumericArrays"/>.
    /// Covers DD-P1-T03 success conditions.
    /// </summary>
    public sealed class JsonAestheticFormatterTests
    {
        // ── Already-flat arrays are preserved ────────────────────────────────

        [Fact]
        public void FlattenNumericArrays_AlreadyFlat_Unchanged()
        {
            // A pure numeric array at the root that is already on one line
            // should come out as a single-line array (possibly with spaces normalized).
            string result = JsonAestheticFormatter.FlattenNumericArrays("[1.0, 2.0, 3.0]");
            // The formatter re-emits via Newtonsoft; verify it is still a single-line array.
            Assert.DoesNotContain("\n", result.Trim());
            Assert.Contains("1.0", result);
            Assert.Contains("2.0", result);
            Assert.Contains("3.0", result);
        }

        // ── Indented numeric array is collapsed ───────────────────────────────

        [Fact]
        public void FlattenNumericArrays_IndentedNumericArray_Collapsed()
        {
            // Simulate the kind of JSON a Position field would produce when written
            // with WriteIndented = true but without a custom converter.
            string input = "{\"Position\":[\n  1.0,\n  2.0,\n  3.0\n]}";
            string result = JsonAestheticFormatter.FlattenNumericArrays(input);

            // The Position value must be on a single line (no newlines inside the array).
            int posIdx    = result.IndexOf("\"Position\"");
            int afterColon = result.IndexOf(':', posIdx) + 1;
            int nextNewLine = result.IndexOf('\n', afterColon);
            int arrayEnd   = result.IndexOf(']', afterColon);

            // The ']' must come before the next newline after the colon — meaning inline.
            Assert.True(arrayEnd < nextNewLine || nextNewLine == -1,
                $"Array was not collapsed to a single line. Got:\n{result}");
        }

        // ── Mixed arrays are NOT collapsed ───────────────────────────────────

        [Fact]
        public void FlattenNumericArrays_MixedArray_NotCollapsed()
        {
            // An array that mixes strings and numbers must NOT be flattened.
            string input = "{\"Tags\":[\"alpha\",1,\"beta\"]}";
            string result = JsonAestheticFormatter.FlattenNumericArrays(input);

            // The result should still contain the values in expanded form —
            // at minimum it must contain multiple lines (or the items are spread).
            // The key assertion: the items are all still present.
            Assert.Contains("\"alpha\"", result);
            Assert.Contains("\"beta\"", result);
            Assert.Contains("1", result);
            // The array should NOT be collapsed to a single raw-value line
            // (it should still have newlines for each item).
            int tagsIdx = result.IndexOf("\"Tags\"");
            int arrayStart = result.IndexOf('[', tagsIdx);
            int arrayEnd   = result.IndexOf(']', arrayStart);
            string arrayContent = result.Substring(arrayStart, arrayEnd - arrayStart + 1);
            // A non-collapsed array will contain newlines inside the [ ... ] region.
            Assert.Contains("\n", arrayContent);
        }

        // ── Empty numeric array is NOT collapsed (boundary) ──────────────────

        [Fact]
        public void FlattenNumericArrays_EmptyArray_NotCollapsedToRawLine()
        {
            // Empty arrays are not "pure numeric" per IsPureNumericArray — they are
            // written normally (expanded start/end array tokens, but trivially empty).
            string input  = "{\"Items\":[]}";
            string result = JsonAestheticFormatter.FlattenNumericArrays(input);
            Assert.Contains("Items", result);
            Assert.Contains("[]", result.Replace("\n", "").Replace(" ", ""));
        }

        // ── Nested objects with numeric arrays ───────────────────────────────

        [Fact]
        public void FlattenNumericArrays_NestedNumericArray_Collapsed()
        {
            string input  = "{\"Entity\":{\"Position\":[10.0,20.0,30.0]}}";
            string result = JsonAestheticFormatter.FlattenNumericArrays(input);
            // Position array must be on a single line inside the nested object.
            Assert.Contains("[10", result);
            // Verify the result still contains the values
            Assert.Contains("10", result);
            Assert.Contains("20", result);
            Assert.Contains("30", result);
        }
    }
}
