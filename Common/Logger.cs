using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SftpTransferAgent.Common
{
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static string _logDir = null;

        public static void Initialize(string logDir)
        {
            _logDir = logDir;
            Directory.CreateDirectory(_logDir);
            Info("Logger initialized. LogDir=" + _logDir);
        }

        public static void Info(string message) => Write("Info", message);
        public static void Warning(string message) => Write("Warning", message);
        public static void Error(string message, Exception ex = null)
            => Write("Error", ex == null ? message : message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var line = $"[{ts}] [{level}] {message}";

            lock (Sync)
            {
                Console.WriteLine(line);

                if (!string.IsNullOrWhiteSpace(_logDir))
                {
                    var file = Path.Combine(_logDir, $"SftpTransferAgent_{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
            }
        }
    }
}