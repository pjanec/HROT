using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fdp.Toolkit.Tkb.Vfs;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests.Vfs
{
    public class RawDirectoryTkbProviderTests : IDisposable
    {
        private readonly string _root;

        public RawDirectoryTkbProviderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "TkbTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        // ---- Helpers ----

        private string CreateJsonFile(string relativePath, string content = "{}")
        {
            var full = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, Encoding.UTF8);
            return full;
        }

        // ---- Tests ----

        [Fact]
        public void EnumerateEntityFiles_ThreeJsonFiles_YieldsExactlyThree()
        {
            CreateJsonFile("A.json");
            CreateJsonFile("B.json");
            CreateJsonFile("C.json");

            var provider = new RawDirectoryTkbProvider(_root);
            var results = EnumerateToList(provider);

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void CategoryPath_NestedTwoLevels_IsForwardSlashRelativeNormalized()
        {
            CreateJsonFile(Path.Combine("Platform", "Vehicle", "MBT.json"));

            var provider = new RawDirectoryTkbProvider(_root);
            var file = EnumerateToList(provider).Single();

            Assert.Equal("Platform/Vehicle", file.CategoryPath);
        }

        [Fact]
        public void FileName_EqualsNameWithoutJsonExtension()
        {
            CreateJsonFile("Merkava Mk4.json");

            var provider = new RawDirectoryTkbProvider(_root);
            var file = EnumerateToList(provider).Single();

            Assert.Equal("Merkava Mk4", file.FileName);
        }

        [Fact]
        public void EnumerateEntityFiles_TxtFileNotYielded()
        {
            CreateJsonFile("Entity.json");
            File.WriteAllText(Path.Combine(_root, "Notes.txt"), "text");

            var provider = new RawDirectoryTkbProvider(_root);
            var results = EnumerateToList(provider);

            Assert.Single(results);
            Assert.Equal("Entity", results[0].FileName);
        }

        [Fact]
        public void EnumerateEntityFiles_SubdirectoryJsonYielded()
        {
            CreateJsonFile(Path.Combine("Sub", "Deep.json"));

            var provider = new RawDirectoryTkbProvider(_root);
            var results = EnumerateToList(provider);

            Assert.Single(results);
            Assert.Equal("Deep", results[0].FileName);
            Assert.Equal("Sub", results[0].CategoryPath);
        }

        [Fact]
        public void WriteEntityFile_ThenEnumerate_ContentRetrievable()
        {
            var provider = new RawDirectoryTkbProvider(_root);
            const string content = """{"Name":"Test"}""";

            provider.WriteEntityFile(Path.Combine("Cat", "Entity.json"), content);

            var results = EnumerateToList(provider, readContent: true);
            var match = results.SingleOrDefault(r => r.fileName == "Entity");
            Assert.NotNull(match.content);
            Assert.Equal(content, match.content);
            Assert.Equal("Cat", match.categoryPath);
        }

        [Fact]
        public void DeleteEntityFile_ExistingFile_RemovedFromEnumeration()
        {
            CreateJsonFile("ToDelete.json");
            var provider = new RawDirectoryTkbProvider(_root);

            provider.DeleteEntityFile("ToDelete.json");

            var results = EnumerateToList(provider);
            Assert.Empty(results);
        }

        [Fact]
        public void DeleteEntityFile_NonexistentPath_DoesNotThrow()
        {
            var provider = new RawDirectoryTkbProvider(_root);
            // Must not throw
            provider.DeleteEntityFile("DoesNotExist.json");
        }

        [Fact]
        public void CategoryPath_AtRoot_IsEmptyString()
        {
            CreateJsonFile("RootEntity.json");

            var provider = new RawDirectoryTkbProvider(_root);
            var file = EnumerateToList(provider).Single();

            Assert.Equal(string.Empty, file.CategoryPath);
            Assert.Equal("RootEntity", file.FileName);
        }

        // ---- Enumeration helpers ----

        // Enumerates and collects file metadata (consuming each stream before advancing).
        private static List<TkbEntityFile> EnumerateToList(RawDirectoryTkbProvider provider)
        {
            var list = new List<TkbEntityFile>();
            foreach (var file in provider.EnumerateEntityFiles())
            {
                // Consume and dispose the stream before advancing the enumerator
                using (file.JsonStream) { }
                list.Add(new TkbEntityFile(file.CategoryPath, file.FileName, Stream.Null));
            }
            return list;
        }

        // Variant that also reads content
        private static List<(string categoryPath, string fileName, string? content)> EnumerateToList(
            RawDirectoryTkbProvider provider, bool readContent)
        {
            var list = new List<(string, string, string?)>();
            foreach (var file in provider.EnumerateEntityFiles())
            {
                string? content = null;
                using (file.JsonStream)
                {
                    if (readContent)
                        content = new StreamReader(file.JsonStream, Encoding.UTF8).ReadToEnd();
                }
                list.Add((file.CategoryPath, file.FileName, content));
            }
            return list;
        }
    }
}
