using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;

namespace Fdp.Tests
{
    /// <summary>
    /// Unit tests for R0.2: Flight Recorder Schema Manifest.
    /// Covers <see cref="ComponentLayoutHasher"/> determinism, sensitivity,
    /// and <see cref="SchemaValidator"/> mismatch detection.
    /// </summary>
    public class FlightRecorderSchemaTests
    {
        // ── Test-only component structs ──────────────────────────────────────────
        // Use IDs in the 240-255 range (reserved block) to avoid conflicts with
        // production component IDs when tests run without an explicit Clear().

        [ComponentId(8)]
        [StructLayout(LayoutKind.Sequential)]
        private struct TwoFieldStruct { public int Field1; public float Field2; }

        [ComponentId(9)]
        [StructLayout(LayoutKind.Sequential)]
        private struct OneFieldStruct { public int Field1; }

        // Same fields as TwoFieldStruct but different type on Field2 → different hash.
        [ComponentId(10)]
        [StructLayout(LayoutKind.Sequential)]
        private struct TwoFieldStructAlt { public int Field1; public int Field2; }

        // Same size, same types as TwoFieldStruct but fields swapped → different hash.
        [ComponentId(11)]
        [StructLayout(LayoutKind.Sequential)]
        private struct SwappedFieldStruct { public float Field2; public int Field1; }

        [ComponentId(12)]
        [StructLayout(LayoutKind.Sequential)]
        private struct ValidatorTargetStruct { public int Value; }

        // ── ComponentLayoutHasher tests ──────────────────────────────────────────

        /// <summary>
        /// Calling ComputeHash on the same type twice must return the same value.
        /// SC: Hash stable across two calls on identical struct (R0.2 SC-6).
        /// </summary>
        [Fact]
        public void ComponentLayoutHasher_StableAcrossMultipleCalls()
        {
            var hash1 = ComponentLayoutHasher.ComputeHash(typeof(TwoFieldStruct));
            var hash2 = ComponentLayoutHasher.ComputeHash(typeof(TwoFieldStruct));

            Assert.Equal(hash1, hash2);
        }

        /// <summary>
        /// A struct with two fields must hash differently from an otherwise identical struct
        /// that has only one of those fields.
        /// SC: Hash changes when field added (R0.2 SC-6).
        /// </summary>
        [Fact]
        public void ComponentLayoutHasher_ChangesWhenFieldAdded()
        {
            var hashOne  = ComponentLayoutHasher.ComputeHash(typeof(OneFieldStruct));
            var hashTwo  = ComponentLayoutHasher.ComputeHash(typeof(TwoFieldStruct));

            Assert.NotEqual(hashOne, hashTwo);
        }

        /// <summary>
        /// Two structs with the same fields in different order must produce different hashes,
        /// because field offsets differ.
        /// SC: Hash changes when fields reordered (R0.2 SC-6).
        /// </summary>
        [Fact]
        public void ComponentLayoutHasher_ChangesWhenFieldsReordered()
        {
            var hashOriginal = ComponentLayoutHasher.ComputeHash(typeof(TwoFieldStruct));
            var hashSwapped  = ComponentLayoutHasher.ComputeHash(typeof(SwappedFieldStruct));

            Assert.NotEqual(hashOriginal, hashSwapped);
        }

        /// <summary>
        /// Two structs with the same field names but different field types must hash differently.
        /// SC: Hash covers field type names (additional sensitivity check).
        /// </summary>
        [Fact]
        public void ComponentLayoutHasher_ChangesWhenFieldTypeChanges()
        {
            var hashFloat = ComponentLayoutHasher.ComputeHash(typeof(TwoFieldStruct));    // Field2 is float
            var hashInt   = ComponentLayoutHasher.ComputeHash(typeof(TwoFieldStructAlt)); // Field2 is int

            Assert.NotEqual(hashFloat, hashInt);
        }

        /// <summary>
        /// ComputeHash must throw ArgumentNullException when given a null type.
        /// SC: Public API validates input (defensive programming).
        /// </summary>
        [Fact]
        public void ComponentLayoutHasher_ThrowsOnNullType()
        {
            Assert.Throws<ArgumentNullException>(() => ComponentLayoutHasher.ComputeHash(null!));
        }

        // ── SchemaValidator tests ────────────────────────────────────────────────

        /// <summary>
        /// Validator must log a warning and return without throwing when SchemaManifest is null.
        /// This ensures backward compatibility with recordings that predate schema manifest support.
        /// SC: Validator succeeds silently when manifest is null (R0.2 SC-6).
        /// </summary>
        [Fact]
        public void SchemaValidator_DoesNotThrow_WhenManifestIsNull()
        {
            var metadata = new RecordingMetadata { SchemaManifest = null };

            // Must complete without exception.
            var ex = Record.Exception(() => SchemaValidator.Validate(metadata));
            Assert.Null(ex);
        }

        /// <summary>
        /// Validator must throw with a descriptive message when the recorded layout hash
        /// does not match the current struct hash.
        /// SC: Validator throws on layout hash mismatch with descriptive message (R0.2 SC-6).
        /// </summary>
        [Fact]
        public void SchemaValidator_ThrowsOnLayoutHashMismatch()
        {
            ComponentTypeRegistry.Clear();
            ComponentTypeRegistry.GetOrRegister<ValidatorTargetStruct>();

            var recordedHash = 0xDEAD_BEEF_CAFE_F00DUL; // Deliberately wrong hash.
            var correctSize  = Marshal.SizeOf<ValidatorTargetStruct>();

            var metadata = new RecordingMetadata
            {
                SchemaManifest = new Dictionary<int, ComponentSchemaInfo>
                {
                    [12] = new ComponentSchemaInfo
                    {
                        Name       = nameof(ValidatorTargetStruct),
                        Size       = correctSize, // Size is correct; only hash is wrong.
                        LayoutHash = recordedHash,
                        IsManaged  = false
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => SchemaValidator.Validate(metadata));

            Assert.Contains("layout has changed", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0xDEADBEEFCAFEF00D", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validator must throw with a descriptive message when the recorded struct size
        /// does not match the current size.
        /// SC: Validator throws on size mismatch (R0.2 SC-6).
        /// </summary>
        [Fact]
        public void SchemaValidator_ThrowsOnSizeMismatch()
        {
            ComponentTypeRegistry.Clear();
            ComponentTypeRegistry.GetOrRegister<ValidatorTargetStruct>();

            var metadata = new RecordingMetadata
            {
                SchemaManifest = new Dictionary<int, ComponentSchemaInfo>
                {
                    [12] = new ComponentSchemaInfo
                    {
                        Name       = nameof(ValidatorTargetStruct),
                        Size       = 999, // Wrong size.
                        LayoutHash = ComponentLayoutHasher.ComputeHash(typeof(ValidatorTargetStruct)),
                        IsManaged  = false
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => SchemaValidator.Validate(metadata));

            Assert.Contains("layout has changed", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("999", ex.Message); // Recorded size mentioned.
        }

        /// <summary>
        /// ⭐ CE-1372 — a component id that NO loaded assembly declares does not refuse the recording: the playback skips it (CE-4050), so
        /// the validator does too. ⛔ SUPERSEDED: <c>SchemaValidator_ThrowsOnUnregisteredComponentId</c> — it threw, and a process that had
        /// merely not REGISTERED a recorded type yet opened an empty world.
        /// </summary>
        [Fact]
        public void CE1372_AComponentIdNoAssemblyDeclares_IsSkipped_NotARefusal()
        {
            ComponentTypeRegistry.Clear();
            var metadata = new RecordingMetadata
            {
                SchemaManifest = new Dictionary<int, ComponentSchemaInfo>
                {
                    [8765] = new ComponentSchemaInfo { Name = "SomeOldComponent", Size = 4, LayoutHash = 0x1234567890ABCDEFUL, IsManaged = false },
                },
            };
            SchemaValidator.Validate(metadata);   // no throw
        }

        [ComponentId(401)]
        public struct Ce1372DeclaredNotRegistered { public int A; public float B; }

        /// <summary>
        /// ⭐ CE-1372 — a type declared by a loaded assembly but not REGISTERED in this process is FOUND by its <c>[ComponentId]</c> and still
        /// validated: the right layout passes, a drifted one is still refused (the check the refusal was for is kept).
        /// </summary>
        [Fact]
        public void CE1372_ADeclaredButUnregisteredComponent_IsFound_AndItsLayoutStillChecked()
        {
            ComponentTypeRegistry.Clear();
            var type = typeof(Ce1372DeclaredNotRegistered);
            RecordingMetadata Meta(int size, ulong hash) => new()
            {
                SchemaManifest = new Dictionary<int, ComponentSchemaInfo>
                {
                    [401] = new ComponentSchemaInfo { Name = type.FullName!, Size = size, LayoutHash = hash, IsManaged = false },
                },
            };
            SchemaValidator.Validate(Meta(TypeLayout.SizeOf(type), ComponentLayoutHasher.ComputeHash(type)));   // found, matches
            var ex = Assert.Throws<InvalidOperationException>(() => SchemaValidator.Validate(Meta(TypeLayout.SizeOf(type) + 4, 0)));
            Assert.Contains("layout has changed", ex.Message);
        }

        /// <summary>
        /// Validator must succeed when the current struct hash and size exactly match
        /// the recorded values.
        /// SC: Valid recording passes validation without exception.
        /// </summary>
        [Fact]
        public void SchemaValidator_Succeeds_WhenSchemaMatches()
        {
            ComponentTypeRegistry.Clear();
            ComponentTypeRegistry.GetOrRegister<ValidatorTargetStruct>();

            var type         = typeof(ValidatorTargetStruct);
            var correctSize  = Marshal.SizeOf(type);
            var correctHash  = ComponentLayoutHasher.ComputeHash(type);

            var metadata = new RecordingMetadata
            {
                SchemaManifest = new Dictionary<int, ComponentSchemaInfo>
                {
                    [12] = new ComponentSchemaInfo
                    {
                        Name       = type.FullName ?? type.Name,
                        Size       = correctSize,
                        LayoutHash = correctHash,
                        IsManaged  = false
                    }
                }
            };

            // Must complete without exception.
            var ex = Record.Exception(() => SchemaValidator.Validate(metadata));
            Assert.Null(ex);
        }
    }
}
