using System;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SftpTransferAgent.Common.Logging
{
    /// <summary>
    /// exe 配下の Log フォルダへ、日次ローテーションでログを書き出すロガー。
    /// ログ出力に失敗しても本処理を落とさない（内部で例外を飲み込む）。
    /// </summary>
    public sealed class RollingFileLogger : ILogger
    {
        private readonly LoggerSettings _settings;

        // 同一プロセス内の並列書き込み対策
        private static readonly object _sync = new object();

        // 複数プロセス（万一の二重起動）でもログ行が混ざりにくいように Mutex を用意
        private readonly Mutex _processMutex;

        public RollingFileLogger(LoggerSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _processMutex = new Mutex(false, BuildGlobalMutexName());
        }

        public void Write(LogLevel level, string message, Exception ex = null)
        {
            try
            {
                if (level < _settings.MinimumLogLevel) return;

                var line = FormatLine(DateTime.Now, level, message, ex);

                // 二重起動/多重書き込み時も破綻しにくいようにプロセス間 Mutex を短時間だけ取る
                var hasMutex = false;
                try
                {
                    hasMutex = _processMutex.WaitOne(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Mutex取得に失敗しても、ログが原因で落とさない
                }

                lock (_sync)
                {
                    WriteWithRetry(line);
                }

                if (hasMutex)
                {
                    try { _processMutex.ReleaseMutex(); } catch { /* noop */ }
                }
            }
            catch
            {
                // ログ出力が原因でアプリが落ちるのを防ぐ
            }
        }

        private void WriteWithRetry(string line)
        {
            // ありがちな一時エラー（共有違反、IO一時失敗）に備えた軽いリトライ
            const int maxRetry = 3;

            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    var dir = GetLogDirectory();
                    Directory.CreateDirectory(dir);

                    var path = GetLogFilePath(dir, DateTime.Now);

                    // 他プロセスからの参照（tail 等）を許す
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    {
                        sw.WriteLine(line);
                        sw.Flush();
                    }
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(100 * (i + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    // 権限不足はリトライしても無駄なケースが多い
                    return;
                }
                catch
                {
                    return;
                }
            }
        }

        private string GetLogDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory; // exe の場所
            return Path.Combine(baseDir, _settings.LogDirectoryName);
        }

        private string GetLogFilePath(string dir, DateTime now)
        {
            var fileName = $"{_settings.LogFilePrefix}-{now:yyyy-MM-dd}.log";
            return Path.Combine(dir, fileName);
        }

        private static string FormatLine(DateTime dt, LogLevel level, string message, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append(dt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
            sb.Append(message ?? "");

            if (ex != null)
            {
                sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                // 運用で調査しやすいよう、スタックトレースも出す（不要なら削除可）
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                {
                    sb.AppendLine();
                    sb.Append(ex.StackTrace);
                }
            }

            return sb.ToString();
        }

        private string BuildGlobalMutexName()
        {
            // 端末内で一意になるように prefix + baseDir をハッシュ化して Mutex 名にする
            var baseDir = (AppDomain.CurrentDomain.BaseDirectory ?? "").ToLowerInvariant();

            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(baseDir));
                var hex = BitConverter.ToString(hash).Replace("-", "");
                return @"Global\SftpTransferAgent_Log_" + hex;
            }
        }
    }
}