using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Fdp.Toolkit.Tkb.Vfs;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests.Vfs
{
    public class ZipTkbProviderTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly List<string> _tempFiles = new();

        public ZipTkbProviderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TkbZipTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                if (File.Exists(f)) File.Delete(f);
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ---- Helpers ----

        /// <summary>
        /// Builds a ZIP at a temp path with the given entries (entry name -> content).
        /// </summary>
        private string CreateTempZip(params (string entryName, string content)[] entries)
        {
            var path = Path.Combine(_tempDir, Path.GetRandomFileName() + ".zip");
            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    var entry = archive.CreateEntry(name);
                    using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                    w.Write(content);
                }
            }
            ms.Position = 0;
            File.WriteAllBytes(path, ms.ToArray());
            _tempFiles.Add(path);
            return path;
        }

        /// <summary>Enumerates provider, reading each stream before advancing.</summary>
        private static List<(string CategoryPath, string FileName, string Content)> Enumerate(
            ZipTkbProvider provider)
        {
            var list = new List<(string, string, string)>();
            foreach (var file in provider.EnumerateEntityFiles())
            {
                string content;
                using (file.JsonStream)
                    content = new StreamReader(file.JsonStream, Encoding.UTF8).ReadToEnd();
                list.Add((file.CategoryPath, file.FileName, content));
            }
            return list;
        }

        // ---- Tests ----

        [Fact]
        public void EnumerateEntityFiles_TwoCategoryPaths_YieldsAllJsonEntries()
        {
            var zipPath = CreateTempZip(
                ("Platform/Vehicle/Tank.json", """{"a":1}"""),
                ("Platform/Aircraft/Jet.json", """{"b":2}"""),
                ("Ground/Infantry/Soldier.json", """{"c":3}"""));

            using var provider = new ZipTkbProvider(zipPath);
            var results = Enumerate(provider);

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void CategoryPath_NestedEntry_IsForwardSlashNormalized()
        {
            var zipPath = CreateTempZip(("Platform/Vehicle/MBT/Merkava.json", "{}"));

            using var provider = new ZipTkbProvider(zipPath);
            var results = Enumerate(provider);

            Assert.Single(results);
            Assert.Equal("Platform/Vehicle/MBT", results[0].CategoryPath);
        }

        [Fact]
        public void FileName_EqualsNameWithoutJsonExtension()
        {
            var zipPath = CreateTempZip(("Platform/M1 Abrams.json", "{}"));

            using var provider = new ZipTkbProvider(zipPath);
            var results = Enumerate(provider);

            Assert.Single(results);
            Assert.Equal("M1 Abrams", results[0].FileName);
        }

        [Fact]
        public void WriteEntityFile_ThrowsNotSupportedException()
        {
            var zipPath = CreateTempZip(("Entry.json", "{}"));
            using var provider = new ZipTkbProvider(zipPath);

            var ex = Assert.Throws<NotSupportedException>(() =>
                provider.WriteEntityFile("New.json", "{}"));
            Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DeleteEntityFile_ThrowsNotSupportedException()
        {
            var zipPath = CreateTempZip(("Entry.json", "{}"));
            using var provider = new ZipTkbProvider(zipPath);

            var ex = Assert.Throws<NotSupportedException>(() =>
                provider.DeleteEntityFile("Entry.json"));
            Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Constructor_OpensArchiveWithReadMode()
        {
            var zipPath = CreateTempZip(("Entry.json", "{}"));
            using var provider = new ZipTkbProvider(zipPath);

            // Verify via reflection that the internal ZipArchive is in Read mode
            var archiveField = typeof(ZipTkbProvider)
                .GetField("_archive", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var archive = (ZipArchive)archiveField.GetValue(provider)!;
            Assert.Equal(ZipArchiveMode.Read, archive.Mode);
        }

        [Fact]
        public void EnumerateEntityFiles_DirectoryMarkers_AreSkipped()
        {
            // Build a ZIP that contains a directory marker entry (ends with '/')
            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Directory marker
                archive.CreateEntry("Platform/Vehicle/");
                // Real entry
                var entry = archive.CreateEntry("Platform/Vehicle/Tank.json");
                using (var w = new StreamWriter(entry.Open()))
                    w.Write("{}");
            }
            ms.Position = 0;
            var zipPath = Path.Combine(_tempDir, "dirmarker.zip");
            File.WriteAllBytes(zipPath, ms.ToArray());
            _tempFiles.Add(zipPath);

            using var provider = new ZipTkbProvider(zipPath);
            var results = Enumerate(provider);

            Assert.Single(results);
            Assert.Equal("Tank", results[0].FileName);
        }

        [Fact]
        public void EnumerateEntityFiles_NonJsonEntries_AreSkipped()
        {
            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Each entry stream must be disposed before creating the next entry
                var txt = archive.CreateEntry("readme.txt");
                using (var wt = new StreamWriter(txt.Open()))
                    wt.Write("text");

                var json = archive.CreateEntry("Entity.json");
                using (var wj = new StreamWriter(json.Open()))
                    wj.Write("{}");
            }
            ms.Position = 0;
            var zipPath = Path.Combine(_tempDir, "mixed.zip");
            File.WriteAllBytes(zipPath, ms.ToArray());
            _tempFiles.Add(zipPath);

            using var provider = new ZipTkbProvider(zipPath);
            var results = Enumerate(provider);

            Assert.Single(results);
            Assert.Equal("Entity", results[0].FileName);
        }

        [Fact]
        public void EnumerateEntityFiles_ZipFromDirectory_MatchesDirectoryProvider()
        {
            // 1. Create source directory with JSON files
            var srcDir = Path.Combine(_tempDir, "src_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(srcDir, "Cat", "Sub"));
            File.WriteAllText(Path.Combine(srcDir, "Root.json"), """{"id":1}""");
            File.WriteAllText(Path.Combine(srcDir, "Cat", "Alpha.json"), """{"id":2}""");
            File.WriteAllText(Path.Combine(srcDir, "Cat", "Sub", "Beta.json"), """{"id":3}""");

            // 2. Pack to ZIP
            var zipPath = Path.Combine(_tempDir, "packed_" + Guid.NewGuid().ToString("N") + ".zip");
            ZipFile.CreateFromDirectory(srcDir, zipPath);
            _tempFiles.Add(zipPath);

            // 3. Enumerate both
            var dirProvider = new RawDirectoryTkbProvider(srcDir);
            var dirResults = new List<(string, string)>();
            foreach (var f in dirProvider.EnumerateEntityFiles())
            {
                using (f.JsonStream) { }
                dirResults.Add((f.CategoryPath, f.FileName));
            }

            using var zipProvider = new ZipTkbProvider(zipPath);
            var zipResults = Enumerate(zipProvider)
                .Select(r => (r.CategoryPath, r.FileName))
                .ToList();

            // 4. Both should have the same logical entities (order may differ)
            dirResults.Sort();
            zipResults.Sort();
            Assert.Equal(dirResults, zipResults);
        }
    }
}
