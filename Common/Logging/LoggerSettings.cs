using System;

namespace SftpTransferAgent.Common.Logging
{
    public sealed class LoggerSettings
    {
        public string LogDirectoryName { get; }
        public string LogFilePrefix { get; }
        public LogLevel MinimumLogLevel { get; }

        public LoggerSettings(string logDirectoryName, string logFilePrefix, LogLevel minimumLogLevel)
        {
            LogDirectoryName = string.IsNullOrWhiteSpace(logDirectoryName) ? "Log" : logDirectoryName.Trim();
            LogFilePrefix = string.IsNullOrWhiteSpace(logFilePrefix) ? "SftpTransferAgent" : logFilePrefix.Trim();
            MinimumLogLevel = minimumLogLevel;
        }
    }
}