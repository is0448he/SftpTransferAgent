using System;
using System.Configuration;

namespace SftpTransferAgent.Common.Logging
{
    /// <summary>
    /// アプリ全体で使う静的ロガー窓口
    /// </summary>
    public static class Logger
    {
        private static ILogger _current;

        public static void InitializeFromAppConfig()
        {
            var dirName = ConfigurationManager.AppSettings["LogDirectoryName"] ?? "Log";
            var prefix = ConfigurationManager.AppSettings["LogFilePrefix"] ?? "SftpTransferAgent";
            var minLvStr = ConfigurationManager.AppSettings["MinimumLogLevel"] ?? "Info";

            LogLevel minLv;
            if (!Enum.TryParse(minLvStr, ignoreCase: true, result: out minLv))
                minLv = LogLevel.Info;

            var settings = new LoggerSettings(dirName, prefix, minLv);
            _current = new RollingFileLogger(settings);
        }

        public static void Debug(string message) => Write(LogLevel.Debug, message, null);
        public static void Info(string message) => Write(LogLevel.Info, message, null);
        public static void Warning(string message) => Write(LogLevel.Warning, message, null);
        public static void Error(string message, Exception ex = null) => Write(LogLevel.Error, message, ex);
        public static void Fatal(string message, Exception ex = null) => Write(LogLevel.Fatal, message, ex);

        public static void Write(LogLevel level, string message, Exception ex)
        {
            // Initialize前でも落とさない
            var logger = _current;
            if (logger == null) return;

            logger.Write(level, message, ex);
        }
    }
}