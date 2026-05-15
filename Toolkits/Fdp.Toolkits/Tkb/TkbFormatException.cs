using System;

namespace Fdp.Toolkit.Tkb
{
    /// <summary>
    /// Thrown when a TKB entity file has structural problems that prevent parsing.
    /// </summary>
    public sealed class TkbFormatException : Exception
    {
        public TkbFormatException(string message) : base(message) { }
        public TkbFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
