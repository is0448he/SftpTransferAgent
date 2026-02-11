using System;
using System.Configuration;

namespace SftpTransferAgent.Common
{
    public sealed class CommonSettingValues
    {
        // ポーリング
        public bool PollingEnabled { get; private set; }
        public int PollingIntervalMillSec { get; private set; }

        // リトライ
        public int RetryMaxCount { get; private set; }
        public int RetryIntervalMilliSec { get; private set; }

        // ディレクトリ
        public string RecvRemoteDir { get; private set; }
        public string SendRemoteDir { get; private set; }
        public string RecvLocalDir { get; private set; }
        public string SendLocalDir { get; private set; }

        // 認証
        public string AuthType { get; private set; }
        public string PrivateKeyPath { get; private set; }

        // SFTP接続
        public string SftpHostName { get; private set; }
        public int SftpPort { get; private set; }
        public string SftpUserName { get; private set; }
        public string SftpPass { get; private set; }
        public int SftpConnectTimeoutSec { get; private set; }
        public int SftpTransferTimeoutSec { get; private set; }

        // ファイル名
        public string RecvZipFileName { get; private set; }
        public string CompleteFileName { get; private set; }

        // ログ
        public string LogLevel { get; private set; }

        private CommonSettingValues() { }

        public static CommonSettingValues Load()
        {
            return new CommonSettingValues
            {
                PollingEnabled = GetBool("PollingEnabled"),
                PollingIntervalMillSec = GetInt("PollingIntervalMillSec"),

                RetryMaxCount = GetInt("RetryMaxCount"),
                RetryIntervalMilliSec = GetInt("RetryIntervalMilliSec"),

                RecvRemoteDir = GetString("RecvRemoteDir"),
                SendRemoteDir = GetString("SendRemoteDir"),
                RecvLocalDir = GetString("RecvLocalDir"),
                SendLocalDir = GetString("SendLocalDir"),

                AuthType = GetString("AuthType"),
                PrivateKeyPath = GetString("PrivateKeyPath"),

                SftpHostName = GetString("SftpHostName"),
                SftpPort = GetInt("SftpPort"),
                SftpUserName = GetString("SftpUserName"),
                SftpPass = GetString("SftpPass"),
                SftpConnectTimeoutSec = GetInt("SftpConnectTimeoutSec"),
                SftpTransferTimeoutSec = GetInt("SftpTransferTimeoutSec"),

                RecvZipFileName = GetString("RecvZipFileName"),
                CompleteFileName = GetString("CompleteFileName"),

                LogLevel = GetString("LogLevel")
            };
        }

        #region helper

        private static string GetString(string key)
        {
            var value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
                throw new ConfigurationErrorsException($"App.config key '{key}' is missing or empty.");
            return value;
        }

        private static int GetInt(string key)
        {
            var value = GetString(key);
            if (!int.TryParse(value, out var result))
                throw new ConfigurationErrorsException($"App.config key '{key}' is not a valid int.");
            return result;
        }

        private static bool GetBool(string key)
        {
            var value = GetString(key);
            if (!bool.TryParse(value, out var result))
                throw new ConfigurationErrorsException($"App.config key '{key}' is not a valid bool.");
            return result;
        }

        #endregion
    }
}
