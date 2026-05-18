using Fbt;
using Fbt.Tests.TestFixtures;
using System;
using System.Reflection;
using Xunit;

namespace Fbt.Tests.Unit
{
    public class DefinitionGeneratorTests
    {
        [Fact]
        public void FbtTreeCatalog_GetSample_BT_ReturnsNonNullBlob()
        {
            var catalogType = Type.GetType("Fbt.Tests.Generated.FbtTreeCatalog, Fbt.Tests");
            Assert.NotNull(catalogType);

            var method = catalogType!.GetMethod("GetSample_BT", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            var blob = method!.Invoke(null, null) as BehaviorTreeBlob;
            Assert.NotNull(blob);
            Assert.True(blob!.Nodes.Length > 0);
        }

        [Fact]
        public void FbtTreeCatalog_GetSample_BT_MatchesDirectCompile()
        {
            var catalogType = Type.GetType("Fbt.Tests.Generated.FbtTreeCatalog, Fbt.Tests");
            Assert.NotNull(catalogType);

            var method = catalogType!.GetMethod("GetSample_BT", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            var catalogBlob = method!.Invoke(null, null) as BehaviorTreeBlob;
            Assert.NotNull(catalogBlob);

            var directBlob = SampleTreeDefinitions.BuildSampleTree();

            Assert.Equal(directBlob.StructureHash, catalogBlob!.StructureHash);
        }

        [Fact]
        public void FbtTreeCatalog_IsStaticClass()
        {
            var catalogType = Type.GetType("Fbt.Tests.Generated.FbtTreeCatalog, Fbt.Tests");
            Assert.NotNull(catalogType);

            // Static classes appear as abstract + sealed in reflection
            Assert.True(catalogType!.IsAbstract && catalogType.IsSealed);
        }

        [Fact]
        public void FbtTreeCatalog_GetSample_BT_MethodExists()
        {
            var catalogType = Type.GetType("Fbt.Tests.Generated.FbtTreeCatalog, Fbt.Tests");
            Assert.NotNull(catalogType);

            var method = catalogType!.GetMethod("GetSample_BT", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);
        }
    }
}
