using Xunit;
using Fdp.Examples.NetworkDemo.Translators;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.Network.Cyclone.Translators;
using Fdp.Core;
using Fdp.Network.Cyclone;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Replication.Services;
using Moq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Fdp.Examples.NetworkDemo.Tests.Translators
{
    public class FireEventTranslatorTests
    {
        // Helper to access protected methods and bypass strict constructor
        private class TestableFireEventTranslator : FireEventTranslator
        {
            // Pass nulls to base - we won't call this constructor at runtime via Create()
            public TestableFireEventTranslator() : base(null!, null!) { }

            public static TestableFireEventTranslator Create(NetworkEntityMap map)
            {
                var obj = (TestableFireEventTranslator)FormatterServices.GetUninitializedObject(typeof(TestableFireEventTranslator));
                
                // Set 'EntityMap' in CycloneNativeEventTranslator (Grandparent)
                // Hierarchy: Testable -> FireEventTranslator -> CycloneNativeEventTranslator<T,U>
                // Note: GetField searches specific type. CycloneNativeEventTranslator<T,U> defines EntityMap.
                var baseType = typeof(CycloneNativeEventTranslator<FireInteractionEvent, NetworkFireEvent>);
                var fieldMap = baseType.GetField("EntityMap", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fieldMap != null) fieldMap.SetValue(obj, map);

                return obj;
            }

            public bool PublicTryEncode(in FireInteractionEvent e, out NetworkFireEvent d) => TryEncode(in e, out d);
            public bool PublicTryDecode(in NetworkFireEvent d, out FireInteractionEvent e) => TryDecode(in d, out e);
        }

        [Fact]
        public void TryEncode_BothEntitiesValid_ReturnsTrue()
        {
            var map = new NetworkEntityMap();
            var attacker = new Entity(1, 0);
            var target = new Entity(2, 0);
            map.Register(100, attacker);
            map.Register(200, target);

            var evt = new FireInteractionEvent
            {
                AttackerRoot = attacker,
                TargetRoot = target,
                WeaponInstanceId = 1,
                Damage = 10
            };

            var translator = TestableFireEventTranslator.Create(map);
            
            bool result = translator.PublicTryEncode(in evt, out var dds);

            Assert.True(result);
            Assert.Equal(100, dds.AttackerNetId);
            Assert.Equal(200, dds.TargetNetId);
            Assert.Equal(1, dds.WeaponInstanceId);
            Assert.Equal(10, dds.Damage);
        }

        [Fact]
        public void TryDecode_TargetNotFound_ReturnsTrueWithNullTarget()
        {
            var map = new NetworkEntityMap();
            var attacker = new Entity(1, 0);
            map.Register(100, attacker);
            // Target 200 not registered

            var dds = new NetworkFireEvent
            {
                AttackerNetId = 100,
                TargetNetId = 200, // Missing
                WeaponInstanceId = 1,
                Damage = 10
            };

            var translator = TestableFireEventTranslator.Create(map);
            
            bool result = translator.PublicTryDecode(in dds, out var evt);

            Assert.True(result);
            Assert.Equal(attacker, evt.AttackerRoot);
            Assert.Equal(Entity.Null, evt.TargetRoot); 
            Assert.Equal(10, evt.Damage);
        }
    }
}
