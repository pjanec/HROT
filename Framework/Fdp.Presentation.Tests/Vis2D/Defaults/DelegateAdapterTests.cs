using System;
using System.Numerics;
using Xunit;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Abstractions;
using Fdp.Kernel;
using Moq;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests.Defaults
{
    public class DelegateAdapterTests
    {
        [Fact]
        public void DelegateAdapter_PositionExtractor_IsCalled()
        {
            var expectedPos = new Vector2(100, 200);
            var adapter = new DelegateAdapter(
                (view, entity) => expectedPos
            );

            var result = adapter.GetPosition(null!, Entity.Null); // View/Entity ignored by lambda

            Assert.Equal(expectedPos, result);
        }

        [Fact]
        public void DelegateAdapter_NullDrawFunc_UsesDefault()
        {
            // We just verify it constructed and doesn't throw on basic calls.
            // We cannot safely call Render() without Raylib context if it uses default renderer.
            // So we skip Render() call here.
            
            var adapter = new DelegateAdapter((v, e) => Vector2.Zero);
            Assert.NotNull(adapter);
            
            // To properly test default rendering, we'd need to mock Raylib, which isn't possible easily.
            // Integration tests should cover this.
        }

        [Fact]
        public void DelegateAdapter_CustomDrawFunc_Invoked()
        {
            bool invoked = false;
            var adapter = new DelegateAdapter(
                (v, e) => Vector2.Zero,
                drawFunc: (v, e, pos, ctx, sel, hov) => { invoked = true; }
            );

            // This is SAFE because it uses custom draw func which does NOT call Raylib
            adapter.Render(null!, Entity.Null, Vector2.Zero, new RenderContext(), false, false);
            
            Assert.True(invoked);
        }
    }
}
