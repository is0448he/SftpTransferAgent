using System;
using System.Configuration;
using System.Security.Cryptography;

namespace SftpTransferAgent.Common
{
    public sealed class CommonSettingValues
    {
        // 1回だけ読み込み（スレッドセーフ）
        private static readonly Lazy<CommonSettingValues> _current =
            new Lazy<CommonSettingValues>(LoadCore, isThreadSafe: true);

        /// <summary>設定値（キャッシュ）</summary>
        public static CommonSettingValues Current => _current.Value;

        #region プロパティ定義
        #region ポーリング制御
        /// <summary>
        /// ポーリング実行可否
        /// - true : 指定間隔で処理を繰り返す（常駐ポーリング）
        /// - false: 1回のみ実行して終了
        /// App.config: PollingEnabled
        /// </summary>
        public bool PollingEnabled { get; private set; }
        /// <summary>
        /// ポーリング間隔（ミリ秒）
        /// </summary>
        public int PollingIntervalMillSec { get; private set; }
        #endregion

        #region リトライ
        /// <summary>
        /// リトライ最大回数
        /// </summary>
        public int RetryMaxCount { get; private set; }
        /// <summary>
        /// リトライ間隔（ミリ秒）
        /// </summary>
        public int RetryIntervalMilliSec { get; private set; }
        #endregion

        #region ディレクトリ
        /// <summary>
        /// SFTP受信ディレクトリ（リモート）
        /// - リモートサーバ上の受信対象ファイル配置先
        /// </summary>
        public string RecvRemoteDir { get; private set; }
        /// <summary>
        /// SFTP送信ディレクトリ（リモート）
        /// - リモートサーバ上の送信先ディレクトリ
        /// </summary>
        public string SendRemoteDir { get; private set; }
        /// <summary>
        /// 【7】受信ローカルディレクトリ
        /// - GET（ダウンロード）したファイルの保存先
        /// </summary>
        public string RecvLocalDir { get; private set; }
        /// <summary>
        /// 送信ローカルディレクトリ
        /// - PUT（アップロード）対象ファイルの配置先
        /// </summary>
        public string SendLocalDir { get; private set; }
        #endregion

        #region 認証
        /// <summary>
        /// 認証方式
        /// - Password : パスワード認証
        /// - PrivateKey : 鍵認証
        /// </summary>
        public string AuthType { get; private set; }
        /// <summary>
        /// 秘密鍵ファイルパス
        /// </summary>
        public string PrivateKeyPath { get; private set; }
        #endregion

        #region SFTP接続
        /// <summary>
        /// SFTPホスト名
        /// </summary>
        public string SftpHostName { get; private set; }
        /// <summary>
        /// SFTPポート番号
        /// </summary>
        public int SftpPort { get; private set; }
        /// <summary>
        /// SFTPユーザ名
        /// </summary>
        public string SftpUserName { get; private set; }
        /// <summary>
        /// SFTPパスワード
        /// </summary>
        public string SftpPass { get; private set; }
        /// <summary>
        /// 接続タイムアウト（秒）
        /// </summary>
        public int SftpConnectTimeoutSec { get; private set; }
        /// <summary>
        /// 転送タイムアウト（秒）
        /// </summary>
        public int SftpTransferTimeoutSec { get; private set; }
        #endregion

        #region ファイル名
        /// <summary>
        /// 受信対象ZIPファイル名
        /// </summary>
        public string RecvZipFileName { get; private set; }
        /// <summary>
        /// 完了ファイル名
        /// </summary>
        public string CompleteFileName { get; private set; }
        #endregion

        #region ログ
        /// <summary>
        /// ログレベル
        /// - Logger 側で解釈して出力レベルを制御する
        /// </summary>
        public string LogLevel { get; private set; }
        #endregion

        #endregion

        private CommonSettingValues() { }

        private static CommonSettingValues LoadCore()
        {
            var authType = GetString("AuthType");

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

                AuthType = authType,
                PrivateKeyPath = GetString("PrivateKeyPath"),

                SftpHostName = GetString("SftpHostName"),
                SftpPort = GetInt("SftpPort"),
                SftpUserName = GetString("SftpUserName"),
                SftpPass = ResolveSftpPass(authType),
                SftpConnectTimeoutSec = GetInt("SftpConnectTimeoutSec"),
                SftpTransferTimeoutSec = GetInt("SftpTransferTimeoutSec"),

                RecvZipFileName = GetString("RecvZipFileName"),
                CompleteFileName = GetString("CompleteFileName"),

                LogLevel = GetString("LogLevel")
            };
        }

        /// <summary>
        /// SftpPassEnc があれば復号して採用、なければ SftpPass（平文）を採用する
        /// </summary>
        private static string ResolveSftpPass(string authType)
        {
            var enc = ConfigurationManager.AppSettings["SftpPassEnc"];
            if (!string.IsNullOrWhiteSpace(enc))
            {
                try
                {
                    // CryptoUtility/PowerShellと合わせて LocalMachine 固定
                    return CryptoUtility.DecryptFromBase64(enc, DataProtectionScope.LocalMachine);
                }
                catch (Exception ex)
                {
                    throw new ConfigurationErrorsException("App.config key 'SftpPassEnc' cannot be decrypted.", ex);
                }
            }

            var plain = ConfigurationManager.AppSettings["SftpPass"] ?? string.Empty;

            // Password 認証時だけ必須チェック（PrivateKeyなら空でもOK）
            if (string.Equals(authType, "Password", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(plain))
            {
                throw new ConfigurationErrorsException("SftpPass is required for Password auth. Set SftpPassEnc or SftpPass.");
            }

            return plain;
        }

        #region Helper

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
