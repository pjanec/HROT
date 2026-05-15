using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Fdp.Toolkit.Tkb.Vfs;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests.Vfs
{
    public class TkbUnifiedLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public TkbUnifiedLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TkbLoaderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ---- Helpers ----

        private string CreateTempZipWithEntry(string extension = ".zip")
        {
            var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("Entity.json");
                using (var w = new StreamWriter(entry.Open(), Encoding.UTF8))
                    w.Write("""{"id":1}""");
            }
            ms.Position = 0;
            var path = Path.Combine(_tempDir, "test" + extension);
            File.WriteAllBytes(path, ms.ToArray());
            return path;
        }

        private string CreateTempDirectoryWithJson()
        {
            var dir = Path.Combine(_tempDir, "tkb_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Entity.json"), """{"id":1}""");
            return dir;
        }

        // ---- Tests ----

        [Fact]
        public void GivenZipPath_EnumerateEntityFiles_ReturnsResults()
        {
            var zipPath = CreateTempZipWithEntry(".zip");
            using var loader = new TkbUnifiedLoader(zipPath);

            var results = loader.EnumerateEntityFiles().ToList();

            Assert.NotEmpty(results);
        }

        [Fact]
        public void GivenDirectoryPath_EnumerateEntityFiles_ReturnsResults()
        {
            var dir = CreateTempDirectoryWithJson();
            using var loader = new TkbUnifiedLoader(dir);

            var results = loader.EnumerateEntityFiles().ToList();

            Assert.NotEmpty(results);
        }

        [Fact]
        public void GivenZipPathUppercaseExtension_StillUsesZipProvider()
        {
            // .ZIP uppercase — must still be handled case-insensitively
            var zipPath = CreateTempZipWithEntry(".ZIP");
            using var loader = new TkbUnifiedLoader(zipPath);

            var results = loader.EnumerateEntityFiles().ToList();

            Assert.NotEmpty(results);
        }

        [Fact]
        public void GivenNonexistentPath_ThrowsArgumentException()
        {
            var badPath = Path.Combine(_tempDir, "does_not_exist.zip");

            Assert.Throws<ArgumentException>(() => new TkbUnifiedLoader(badPath));
        }

        [Fact]
        public void GivenExistingTxtFile_ThrowsArgumentException()
        {
            var txtPath = Path.Combine(_tempDir, "file.txt");
            File.WriteAllText(txtPath, "text");

            Assert.Throws<ArgumentException>(() => new TkbUnifiedLoader(txtPath));
        }

        [Fact]
        public void Dispose_CanBeCalledWithoutError()
        {
            var dir = CreateTempDirectoryWithJson();
            var loader = new TkbUnifiedLoader(dir);

            // Must not throw
            loader.Dispose();
        }

        [Fact]
        public void GivenZipPath_EntityFileName_IsCorrect()
        {
            var zipPath = CreateTempZipWithEntry(".zip");
            using var loader = new TkbUnifiedLoader(zipPath);

            string? foundName = null;
            foreach (var file in loader.EnumerateEntityFiles())
            {
                using (file.JsonStream) { }
                foundName = file.FileName;
            }

            Assert.Equal("Entity", foundName);
        }

        [Fact]
        public void GivenDirectory_EntityFileName_IsCorrect()
        {
            var dir = CreateTempDirectoryWithJson();
            using var loader = new TkbUnifiedLoader(dir);

            string? foundName = null;
            foreach (var file in loader.EnumerateEntityFiles())
            {
                using (file.JsonStream) { }
                foundName = file.FileName;
            }

            Assert.Equal("Entity", foundName);
        }
    }
}
