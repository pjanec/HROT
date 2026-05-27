using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Navigation;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
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
#pragma warning disable CS0618
            var ids = new[]
            {
                NavigationConstants.ActionIdMoveTo,
                NavigationConstants.ActionIdFlee,
                NavigationConstants.ActionIdFollowRoute,
                NavigationConstants.ActionIdFollowRoadGraph,
                NavigationConstants.ActionIdPlanRoute,
                NavigationConstants.ActionIdFollowPath,
                NavigationConstants.ActionIdFetchPathDetails,
                NavigationConstants.ActionIdReleasePath,
            };
#pragma warning restore CS0618

            var distinct = new System.Collections.Generic.HashSet<ushort>(ids);
            Assert.Equal(ids.Length, distinct.Count);
        }

        // ── FleeParams.Threat field type guard ────────────────────────────────────

        /// <summary>MoveToParams must be exactly 32 bytes.</summary>
        [Fact]
        public unsafe void MoveToParams_SizeIsAtMost32Bytes()
        {
            Assert.True(sizeof(MoveToParams) <= 32,
                $"MoveToParams is {sizeof(MoveToParams)} bytes — exceeds 32-byte channel limit.");
            Assert.Equal(32, sizeof(MoveToParams));
        }

        /// <summary>PlanRouteParams must be exactly 32 bytes.</summary>
        [Fact]
        public unsafe void PlanRouteParams_SizeIs32Bytes()
        {
            Assert.Equal(32, sizeof(PlanRouteParams));
        }

        /// <summary>FollowPathParams must be exactly 32 bytes.</summary>
        [Fact]
        public unsafe void FollowPathParams_SizeIs32Bytes()
        {
            Assert.Equal(32, sizeof(FollowPathParams));
        }

        /// <summary>FetchPathDetailsParams must be exactly 32 bytes.</summary>
        [Fact]
        public unsafe void FetchPathDetailsParams_SizeIs32Bytes()
        {
            Assert.Equal(32, sizeof(FetchPathDetailsParams));
        }

        /// <summary>ReleasePathParams must be exactly 32 bytes.</summary>
        [Fact]
        public unsafe void ReleasePathParams_SizeIs32Bytes()
        {
            Assert.Equal(32, sizeof(ReleasePathParams));
        }

        /// <summary>
        /// The new NavigationResult values must not collide with the original values.
        /// </summary>
        [Fact]
        public void NavigationResult_NewValuesNotColliding()
        {
            var values = (NavigationResult[])Enum.GetValues(typeof(NavigationResult));
            var nums   = new System.Collections.Generic.HashSet<int>();
            foreach (var v in values)
                Assert.True(nums.Add((int)v), $"Duplicate NavigationResult value: {v} = {(int)v}");
        }

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
