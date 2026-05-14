using System;

using Microsoft.Extensions.Logging;

namespace Trino.Client.Logging
{
    // Wraps logger extensions to avoid direct dependency on Microsoft.Extensions.Logging
    public static class LoggerExtensions
    {
        private static readonly Func<string, Exception, string> _messageFormatter = (state, _) => state;

        private static string Format(string message, object[] args) =>
            args == null || args.Length == 0 ? message : string.Format(message, args);

        /// <summary>Formats and writes a debug log message.</summary>
        public static void LogDebug(
          this ILoggerWrapper logger,
          EventId eventId,
          Exception exception,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Debug, eventId, Format(message, args), exception, _messageFormatter);
        }

        /// <summary>Formats and writes a debug log message.</summary>
        public static void LogDebug(
          this ILoggerWrapper logger,
          EventId eventId,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Debug, eventId, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a debug log message.</summary>
        public static void LogDebug(this ILoggerWrapper logger, string message, params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Debug, 0, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a trace log message.</summary>
        public static void LogTrace(
          this ILoggerWrapper logger,
          EventId eventId,
          Exception exception,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Trace, eventId, Format(message, args), exception, _messageFormatter);
        }

        /// <summary>Formats and writes a trace log message.</summary>
        public static void LogTrace(
          this ILoggerWrapper logger,
          EventId eventId,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Trace, eventId, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a trace log message.</summary>
        public static void LogTrace(this ILoggerWrapper logger, string message, params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Trace, 0, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes an informational log message.</summary>
        public static void LogInformation(
          this ILoggerWrapper logger,
          EventId eventId,
          Exception exception,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Information, eventId, Format(message, args), exception, _messageFormatter);
        }

        /// <summary>Formats and writes an informational log message.</summary>
        public static void LogInformation(
          this ILoggerWrapper logger,
          EventId eventId,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Information, eventId, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes an informational log message.</summary>
        public static void LogInformation(this ILoggerWrapper logger, string message, params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Information, 0, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a warning log message.</summary>
        public static void LogWarning(
          this ILoggerWrapper logger,
          EventId eventId,
          Exception exception,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Warning, eventId, Format(message, args), exception, _messageFormatter);
        }

        /// <summary>Formats and writes a warning log message.</summary>
        public static void LogWarning(
          this ILoggerWrapper logger,
          EventId eventId,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Warning, eventId, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a warning log message.</summary>
        public static void LogWarning(this ILoggerWrapper logger, string message, params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Warning, 0, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes an error log message.</summary>
        public static void LogError(
          this ILoggerWrapper logger,
          EventId eventId,
          Exception exception,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Error, eventId, Format(message, args), exception, _messageFormatter);
        }

        /// <summary>Formats and writes an error log message.</summary>
        public static void LogError(
          this ILoggerWrapper logger,
          EventId eventId,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Error, eventId, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes an error log message.</summary>
        public static void LogError(this ILoggerWrapper logger, string message, params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Error, 0, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a critical log message.</summary>
        public static void LogCritical(
          this ILoggerWrapper logger,
          EventId eventId,
          Exception exception,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Critical, eventId, Format(message, args), exception, _messageFormatter);
        }

        /// <summary>Formats and writes a critical log message.</summary>
        public static void LogCritical(
          this ILoggerWrapper logger,
          EventId eventId,
          string message,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Critical, eventId, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats and writes a critical log message.</summary>
        public static void LogCritical(this ILoggerWrapper logger, string message, params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            logger.Log<string>(LogLevel.Critical, 0, Format(message, args), null, _messageFormatter);
        }

        /// <summary>Formats the message and creates a scope.</summary>
        public static IDisposable BeginScope(
          this ILoggerWrapper logger,
          string messageFormat,
          params object[] args)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            return logger.BeginScope<string>(Format(messageFormat, args));
        }
    }
}
