using System;
using System.IO;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>CE-2417</c> — a read-only wrapper whose reads advance the running node's <see cref="LoadProgress"/> (one unit per read call), so a loader that
    /// reads a large file reports while it reads. 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §12.
    /// </summary>
    public sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly LoadProgress? _progress;

        /// <summary>Wraps <paramref name="inner"/>; reads report into the ambient <see cref="LoadProgress.Current"/> captured now.</summary>
        public ProgressStream(Stream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _progress = LoadProgress.Current;
        }

        /// <summary>⭐ The whole file as text, read through a <see cref="ProgressStream"/> — the reporting <c>File.ReadAllText</c>.</summary>
        public static string ReadAllText(string path)
        {
            using var reader = new StreamReader(new ProgressStream(File.OpenRead(path)));
            return reader.ReadToEnd();
        }

        /// <summary>⭐ The whole file as bytes, read through a <see cref="ProgressStream"/> — the reporting <c>File.ReadAllBytes</c>.</summary>
        public static byte[] ReadAllBytes(string path)
        {
            using var file = File.OpenRead(path);
            using var stream = new ProgressStream(file);
            var bytes = new byte[file.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes, read, Math.Min(1 << 20, bytes.Length - read));
                if (n == 0) break;
                read += n;
            }
            return read == bytes.Length ? bytes : bytes.AsSpan(0, read).ToArray();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            _progress?.Advance();
            return n;
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int n = _inner.Read(buffer);
            _progress?.Advance();
            return n;
        }

        /// <inheritdoc/>
        public override bool CanRead => _inner.CanRead;
        /// <inheritdoc/>
        public override bool CanSeek => _inner.CanSeek;
        /// <inheritdoc/>
        public override bool CanWrite => false;
        /// <inheritdoc/>
        public override long Length => _inner.Length;
        /// <inheritdoc/>
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        /// <inheritdoc/>
        public override void Flush() { }
        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
