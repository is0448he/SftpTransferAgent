using SftpTransferAgent.Common;
using Renci.SshNet;
using System;
using System.IO;

namespace SftpTransferAgent.Sftp
{
    /// <summary>
    /// SFTP通信サービス（外部I/F）
    /// </summary>
    public class SftpService
    {
        /// <summary>
        /// ファイル送受信処理を実行する（1回分）
        /// - GET: リモート(RecvRemoteDir/RecvZipFileName) -> ローカル(RecvLocalDir/RecvZipFileName)
        /// - PUT: ローカル(SendLocalDir/CompleteFileName) -> リモート(SendRemoteDir/CompleteFileName)
        /// </summary>
        /// <param name="settings">設定値</param>
        /// <returns>True:正常 / False:異常（Controller側でリトライ対象）</returns>
        public bool Execute(CommonSettingValues settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            try
            {
                using (var client = CreateSftpClient(settings))
                {
                    client.Connect();

                    var root = client.ListDirectory("/");
                    foreach (var f in root)
                    {
                        Logger.Info("ROOT: " + f.FullName);
                    }

                    // GET（recv.zip）
                    if (!TryDownloadRecvZip(client, settings))
                        return false;

                    // PUT（download.complete）
                    if (!TryUploadCompleteFile(client, settings))
                        return false;

                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpService] Execute failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// GET：リモートに recv.zip が存在する場合にローカルへダウンロードする
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="settings">設定値</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryDownloadRecvZip(SftpClient client, CommonSettingValues settings)
        {
            try
            {
                EnsureLocalDirectory(settings.RecvLocalDir);

                var remoteZipPath = CombineRemotePath(settings.RecvRemoteDir, settings.RecvZipFileName);
                var localZipPath = Path.Combine(settings.RecvLocalDir, settings.RecvZipFileName);

                if (!client.Exists(remoteZipPath))
                {
                    Logger.Info($"[SftpService] GET skipped (remote not found). remote='{remoteZipPath}'");
                    return true; // 処理なし＝正常
                }

                Logger.Info($"[SftpService] GET start. remote='{remoteZipPath}', local='{localZipPath}'");

                DownloadToLocalAtomic(client, remoteZipPath, localZipPath);

                Logger.Info($"[SftpService] GET success. local='{localZipPath}'");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpService] GET failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// PUT：ローカルに download.complete が存在する場合にリモートへアップロードする
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="settings">設定値</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryUploadCompleteFile(SftpClient client, CommonSettingValues settings)
        {
            try
            {
                EnsureLocalDirectory(settings.SendLocalDir);

                var localCompletePath = Path.Combine(settings.SendLocalDir, settings.CompleteFileName);
                var remoteCompletePath = CombineRemotePath(settings.SendRemoteDir, settings.CompleteFileName);

                if (!File.Exists(localCompletePath))
                {
                    Logger.Info($"[SftpService] PUT skipped (local not found). local='{localCompletePath}'");
                    return true; // 処理なし＝正常
                }

                Logger.Info($"[SftpService] PUT start. local='{localCompletePath}', remote='{remoteCompletePath}'");

                UploadFile(client, localCompletePath, remoteCompletePath);

                Logger.Info($"[SftpService] PUT success. remote='{remoteCompletePath}'");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpService] PUT failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// SftpClient を生成する（認証方式・タイムアウト等を設定）
        /// </summary>
        /// <param name="settings">設定値</param>
        /// <returns>SftpClient</returns>
        private SftpClient CreateSftpClient(CommonSettingValues settings)
        {
            var connectionInfo = BuildConnectionInfo(settings);

            var client = new SftpClient(connectionInfo)
            {
                OperationTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.SftpTransferTimeoutSec)),
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            // ホストキー検証（現状は設定で持っていないので許可。将来ピン止め推奨）
            client.HostKeyReceived += (s, e) =>
            {
                Logger.Warn("[SftpService] HostKeyReceived (NOT validated; consider pinning).");
                e.CanTrust = true;
            };

            return client;
        }

        /// <summary>
        /// ConnectionInfo を構築する（Password / PrivateKey）
        /// </summary>
        /// <param name="settings">設定値</param>
        /// <returns>ConnectionInfo</returns>
        private ConnectionInfo BuildConnectionInfo(CommonSettingValues settings)
        {
            var port = settings.SftpPort <= 0 ? 22 : settings.SftpPort;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.SftpConnectTimeoutSec));

            if (string.Equals(settings.AuthType, "PrivateKey", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
                    throw new InvalidOperationException("PrivateKeyPath is empty for PrivateKey auth.");
                if (!File.Exists(settings.PrivateKeyPath))
                    throw new FileNotFoundException("PrivateKey file not found.", settings.PrivateKeyPath);

                // パスフレーズが必要な鍵の場合は SftpPass を流用（必要なら設定キー分離）
                var keyFile = string.IsNullOrWhiteSpace(settings.SftpPass)
                    ? new PrivateKeyFile(settings.PrivateKeyPath)
                    : new PrivateKeyFile(settings.PrivateKeyPath, settings.SftpPass);

                var auth = new PrivateKeyAuthenticationMethod(settings.SftpUserName, keyFile);

                return new ConnectionInfo(settings.SftpHostName, port, settings.SftpUserName, auth)
                {
                    Timeout = timeout
                };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.SftpPass))
                    throw new InvalidOperationException("SftpPass is empty for Password auth.");

                var auth = new PasswordAuthenticationMethod(settings.SftpUserName, settings.SftpPass);

                return new ConnectionInfo(settings.SftpHostName, port, settings.SftpUserName, auth)
                {
                    Timeout = timeout
                };
            }
        }

        /// <summary>
        /// GET：ローカルにテンポラリで落としてから置換する（途中失敗で壊れたファイルが残らない）
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="remotePath">リモートファイルパス</param>
        /// <param name="localPath">ローカル保存先パス</param>
        private void DownloadToLocalAtomic(SftpClient client, string remotePath, string localPath)
        {
            var tempPath = localPath + ".tmp";

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using (var fs = File.Open(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                client.DownloadFile(remotePath, fs);
            }

            if (File.Exists(localPath))
                File.Delete(localPath);

            File.Move(tempPath, localPath);
        }

        /// <summary>
        /// PUT：ローカルファイルをリモートへアップロードする（上書き）
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="localPath">ローカルファイルパス</param>
        /// <param name="remotePath">リモート保存先パス</param>
        private void UploadFile(SftpClient client, string localPath, string remotePath)
        {
            if (!File.Exists(localPath))
                throw new FileNotFoundException("Local file not found.", localPath);

            using (var fs = File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                client.UploadFile(fs, remotePath, true);
            }
        }

        /// <summary>
        /// ローカルディレクトリが存在しない場合は作成する
        /// </summary>
        /// <param name="dir">ディレクトリパス</param>
        private void EnsureLocalDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                throw new InvalidOperationException("Local directory path is empty.");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// リモートパスを結合する（SSH.NETは基本 '/' 区切り。Windowsっぽい設定値でも吸収）
        /// </summary>
        /// <param name="dir">ディレクトリ</param>
        /// <param name="name">ファイル名</param>
        /// <returns>結合済みパス</returns>
        private string CombineRemotePath(string dir, string name)
        {
            // 例: "C:\SftpServer" + "recv.zip" -> "C:/SftpServer/recv.zip"
            var d = (dir ?? "").Replace('\\', '/').TrimEnd('/');
            var n = (name ?? "").Replace('\\', '/').TrimStart('/');

            if (string.IsNullOrWhiteSpace(d)) d = "/";
            if (string.IsNullOrWhiteSpace(n)) return d;

            return $"{d}/{n}";
        }
    }
}