using System.Runtime.CompilerServices;
using NLog;

namespace Fdp.Core.Logging
{
    /// <summary>
    /// High-performance static logging facade optimized for hot-path usage.
    /// Wraps NLog but exposes boolean flags to avoid allocation of interpolated strings.
    /// Uses generic type parameter for automatic logger naming based on calling type.
    /// </summary>
    /// <typeparam name="T">The context type (usually the class calling the logger)</typeparam>
    public static class FdpLog<T>
    {
        // NLog handles the caching and thread-safety of this logger instance internally
        private static readonly Logger _logger = LogManager.GetLogger(typeof(T).FullName);

        // --- HOT PATH FLAGS ---
        // Accessing these is as fast as a boolean field read.
        // Use these to guard complex string interpolations.
        
        /// <summary>
        /// Gets whether Trace level logging is enabled.
        /// Check this before performing expensive string operations.
        /// </summary>
        public static bool IsTraceEnabled => _logger.IsTraceEnabled;
        
        /// <summary>
        /// Gets whether Debug level logging is enabled.
        /// Check this before performing expensive string operations.
        /// </summary>
        public static bool IsDebugEnabled => _logger.IsDebugEnabled;
        
        /// <summary>
        /// Gets whether Info level logging is enabled.
        /// Check this before performing expensive string operations.
        /// </summary>
        public static bool IsInfoEnabled => _logger.IsInfoEnabled;
        
        /// <summary>
        /// Gets whether Warn level logging is enabled.
        /// Check this before performing expensive string operations.
        /// </summary>
        public static bool IsWarnEnabled => _logger.IsWarnEnabled;

        /// <summary>
        /// Gets whether Error level logging is enabled.
        /// Check this before performing expensive string operations.
        /// </summary>
        public static bool IsErrorEnabled => _logger.IsErrorEnabled;

        // --- LOGGING METHODS ---
        
        /// <summary>
        /// Logs a trace message. Use for detailed execution flow on hot paths.
        /// Consider guarding with IsTraceEnabled check for expensive operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string message)
        {
            if (_logger.IsTraceEnabled) _logger.Trace(message);
        }

        /// <summary>
        /// Logs a trace message with one argument.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string format, object arg0)
        {
            if (_logger.IsTraceEnabled) _logger.Trace(format, arg0);
        }

        /// <summary>
        /// Logs a trace message with two arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string format, object arg0, object arg1)
        {
            if (_logger.IsTraceEnabled) _logger.Trace(format, arg0, arg1);
        }

        /// <summary>
        /// Logs a trace message with three arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string format, object arg0, object arg1, object arg2)
        {
            if (_logger.IsTraceEnabled) _logger.Trace(format, arg0, arg1, arg2);
        }

        /// <summary>
        /// Logs a trace message with four arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string format, object arg0, object arg1, object arg2, object arg3)
        {
            if (_logger.IsTraceEnabled) _logger.Trace(format, arg0, arg1, arg2, arg3);
        }

        /// <summary>
        /// Logs a debug message. Use for important state changes and entity operations.
        /// Consider guarding with IsDebugEnabled check for expensive operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string message)
        {
            if (_logger.IsDebugEnabled) _logger.Debug(message);
        }

        /// <summary>
        /// Logs a debug message with one argument.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string format, object arg0)
        {
            if (_logger.IsDebugEnabled) _logger.Debug(format, arg0);
        }

        /// <summary>
        /// Logs a debug message with two arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string format, object arg0, object arg1)
        {
            if (_logger.IsDebugEnabled) _logger.Debug(format, arg0, arg1);
        }

        /// <summary>
        /// Logs a debug message with three arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string format, object arg0, object arg1, object arg2)
        {
            if (_logger.IsDebugEnabled) _logger.Debug(format, arg0, arg1, arg2);
        }

        /// <summary>
        /// Logs a debug message with four arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string format, object arg0, object arg1, object arg2, object arg3)
        {
            if (_logger.IsDebugEnabled) _logger.Debug(format, arg0, arg1, arg2, arg3);
        }

        /// <summary>
        /// Logs an info message. Use for lifecycle events, mode switches, network discovery.
        /// Consider guarding with IsInfoEnabled check for expensive operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string message)
        {
            if (_logger.IsInfoEnabled) _logger.Info(message);
        }

        /// <summary>
        /// Logs an info message with one argument.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string format, object arg0)
        {
            if (_logger.IsInfoEnabled) _logger.Info(format, arg0);
        }

        /// <summary>
        /// Logs an info message with two arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string format, object arg0, object arg1)
        {
            if (_logger.IsInfoEnabled) _logger.Info(format, arg0, arg1);
        }

        /// <summary>
        /// Logs an info message with three arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string format, object arg0, object arg1, object arg2)
        {
            if (_logger.IsInfoEnabled) _logger.Info(format, arg0, arg1, arg2);
        }

        /// <summary>
        /// Logs an info message with four arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string format, object arg0, object arg1, object arg2, object arg3)
        {
            if (_logger.IsInfoEnabled) _logger.Info(format, arg0, arg1, arg2, arg3);
        }

        /// <summary>
        /// Logs a warning message. Use for recoverable issues, missing data, timeouts.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warn(string message)
        {
            if (_logger.IsWarnEnabled) _logger.Warn(message);
        }

        /// <summary>
        /// Logs a warning message with one argument.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warn(string format, object arg0)
        {
            if (_logger.IsWarnEnabled) _logger.Warn(format, arg0);
        }

        /// <summary>
        /// Logs a warning message with two arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warn(string format, object arg0, object arg1)
        {
            if (_logger.IsWarnEnabled) _logger.Warn(format, arg0, arg1);
        }

        /// <summary>
        /// Logs a warning message with three arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warn(string format, object arg0, object arg1, object arg2)
        {
            if (_logger.IsWarnEnabled) _logger.Warn(format, arg0, arg1, arg2);
        }

        /// <summary>
        /// Logs a warning message with four arguments.
        /// Avoids params array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warn(string format, object arg0, object arg1, object arg2, object arg3)
        {
            if (_logger.IsWarnEnabled) _logger.Warn(format, arg0, arg1, arg2, arg3);
        }

        /// <summary>
        /// Logs an error message. Use for critical failures.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string message)
        {
            _logger.Error(message);
        }

        /// <summary>
        /// Logs an error message with exception details.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string message, Exception ex)
        {
            _logger.Error(ex, message);
        }

        /// <summary>
        /// Logs an error message with one argument.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string format, object arg0)
        {
            if (_logger.IsErrorEnabled) _logger.Error(format, arg0);
        }

        /// <summary>
        /// Logs an error message with two arguments.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string format, object arg0, object arg1)
        {
            if (_logger.IsErrorEnabled) _logger.Error(format, arg0, arg1);
        }

        /// <summary>
        /// Logs an error message with three arguments.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string format, object arg0, object arg1, object arg2)
        {
            if (_logger.IsErrorEnabled) _logger.Error(format, arg0, arg1, arg2);
        }

        /// <summary>
        /// Logs an error message with four arguments.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string format, object arg0, object arg1, object arg2, object arg3)
        {
            if (_logger.IsErrorEnabled) _logger.Error(format, arg0, arg1, arg2, arg3);
        }
    }
}
