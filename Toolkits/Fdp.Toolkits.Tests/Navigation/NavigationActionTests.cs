using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using FDP.Toolkit.Navigation;
using Fdp.Kernel;
using Xunit;

namespace FDP.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for <see cref="NavigationActions"/> parameter structs and
    /// <see cref="NavigationConstants"/> action IDs (BCS-P3-T1).
    /// <para>
    /// These tests enforce the 32-byte channel payload limit and document
    /// structural invariants (e.g. <see cref="FleeParams.Threat"/> must be a
    /// full <see cref="Entity"/>, not a raw <c>int</c>).
    /// </para>
    /// </summary>
    public class NavigationActionTests
    {
        // ── Struct size tests ─────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="MoveToParams"/> must fit in the 32-byte LocomotionChannel payload.
        /// Expected layout: Vector2 (8) + float (4) + float (4) = 16 bytes.
        /// </summary>
        [Fact]
        public unsafe void MoveToParams_SizeWithinChannelLimit()
        {
            Assert.True(sizeof(MoveToParams) <= 32,
                $"MoveToParams is {sizeof(MoveToParams)} bytes — exceeds 32-byte channel limit.");
        }

        /// <summary>
        /// <see cref="FleeParams"/> must fit in the 32-byte LocomotionChannel payload.
        /// Expected layout: Entity (8) + float (4) + float (4) = 16 bytes.
        /// </summary>
        [Fact]
        public unsafe void FleeParams_SizeWithinChannelLimit()
        {
            Assert.True(sizeof(FleeParams) <= 32,
                $"FleeParams is {sizeof(FleeParams)} bytes — exceeds 32-byte channel limit.");
        }

        /// <summary>
        /// <see cref="FleeState"/> must fit in the 32-byte LocomotionChannel payload.
        /// Expected layout: uint (4) = 4 bytes.
        /// </summary>
        [Fact]
        public unsafe void FleeState_SizeWithinChannelLimit()
        {
            Assert.True(sizeof(FleeState) <= 32,
                $"FleeState is {sizeof(FleeState)} bytes — exceeds 32-byte channel limit.");
        }

        /// <summary>
        /// <see cref="FollowRouteParams"/> must fit in the 32-byte LocomotionChannel payload.
        /// Expected layout: int (4) + byte (1) + 3 padding = 8 bytes.
        /// </summary>
        [Fact]
        public unsafe void FollowRouteParams_SizeWithinChannelLimit()
        {
            Assert.True(sizeof(FollowRouteParams) <= 32,
                $"FollowRouteParams is {sizeof(FollowRouteParams)} bytes — exceeds 32-byte channel limit.");
        }

        /// <summary>
        /// <see cref="FollowRoadGraphParams"/> must fit in the 32-byte LocomotionChannel payload.
        /// Expected layout: int (4) + float (4) = 8 bytes.
        /// </summary>
        [Fact]
        public unsafe void FollowRoadGraphParams_SizeWithinChannelLimit()
        {
            Assert.True(sizeof(FollowRoadGraphParams) <= 32,
                $"FollowRoadGraphParams is {sizeof(FollowRoadGraphParams)} bytes — exceeds 32-byte channel limit.");
        }

        // ── Action ID distinctness ────────────────────────────────────────────────

        /// <summary>
        /// All action ID constants must be distinct.
        /// Catches accidental copy-paste if constants are rearranged in the future.
        /// </summary>
        [Fact]
        public void AllNavigationActionIds_AreDistinct()
        {
            var ids = new[]
            {
                NavigationConstants.ActionIdMoveTo,
                NavigationConstants.ActionIdFlee,
                NavigationConstants.ActionIdFollowRoute,
                NavigationConstants.ActionIdFollowRoadGraph,
            };

            var distinct = new System.Collections.Generic.HashSet<ushort>(ids);
            Assert.Equal(ids.Length, distinct.Count);
        }

        // ── FleeParams.Threat field type guard ────────────────────────────────────

        /// <summary>
        /// DEBT-009 guard: <see cref="FleeParams.Threat"/> must be a full <see cref="Entity"/>
        /// struct (8 bytes: 4-byte Index + 2-byte Generation + 2-byte padding), not a raw
        /// <c>int</c> (4 bytes).
        /// <para>
        /// This prevents the same anti-pattern that DEBT-009 fixed in <c>SpatialHashGrid</c>:
        /// storing raw integer indices that bypass generational safety.
        /// </para>
        /// </summary>
        [Fact]
        public unsafe void FleeParams_ContainsFullEntityStruct_NotRawIndex()
        {
            // Entity is 8 bytes (int Index + ushort Generation + 2 bytes padding).
            Assert.Equal(8, sizeof(Entity));

            // The Threat field must be typed as Entity, not int.
            var field = typeof(FleeParams).GetField(nameof(FleeParams.Threat),
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Assert.Equal(typeof(Entity), field!.FieldType);
        }
    }
}
