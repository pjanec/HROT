using System;
using System.Reflection;
using System.Runtime.InteropServices;
using CarKinem.Formation;
using Fdp.Core;
using Xunit;

namespace CarKinem.Tests.DataStructures
{
    public class FormationComponentsTests
    {
        [Fact]
        public void FormationController_HasCorrectComponentId()
        {
            var attr = typeof(FormationController).GetCustomAttribute<ComponentIdAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(GlobalComponentIds.FormationController, attr.Id);
        }

        [Fact]
        public void FormationController_IsBlittable()
        {
            Assert.True(IsBlittable<FormationController>());
        }

        [Fact]
        public void FormationFollower_HasCorrectComponentId()
        {
            var attr = typeof(FormationFollower).GetCustomAttribute<ComponentIdAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(GlobalComponentIds.FormationFollower, attr.Id);
        }

        [Fact]
        public void FormationFollower_IsBlittable()
        {
            Assert.True(IsBlittable<FormationFollower>());
        }

        [Fact]
        public void FormationTarget_IsBlittable()
        {
            Assert.True(IsBlittable<FormationTarget>());
        }

        [Fact]
        public void FormationSlot_IsBlittable()
        {
            Assert.True(IsBlittable<FormationSlot>());
        }

        [Fact]
        public void FormationEnums_Sizes()
        {
            Assert.Equal(1, sizeof(FormationType));
            Assert.Equal(1, sizeof(FormationMemberState));
        }

        private static bool IsBlittable<T>() where T : struct
        {
            try
            {
                var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
                Marshal.FreeHGlobal(ptr);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
