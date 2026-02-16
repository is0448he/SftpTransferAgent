using Renci.SshNet;
using SftpTransferAgent.Common;
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

            var recvPram = CreateRecvPram(settings);
            var sendPram = CreateSendPram(settings);

            try
            {
                using (var client = CreateSftpClient(settings))
                {
                    client.Connect();

                    // GET（recv.zip）
                    if (!TryDownloadRecvFile(client, recvPram))
                        return false;

                    // PUT（download.complete）
                    if (!TryUploadCompleteFile(client, sendPram))
                        return false;

                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpTransferAgent] Execute failed.", ex);
                return false;
            }
        }

        #region GET
        /// <summary>
        /// GET：リモートに recv.zip が存在する場合にローカルへダウンロードする
        /// 設定値からGET（受信）処理用パラメータを生成する。
        /// </summary>
        /// <param name="settings">設定値。</param>
        /// <returns>受信処理用パラメータ。</returns>
        private SftpRecvPram CreateRecvPram(CommonSettingValues settings)
        {
            return new SftpRecvPram
            {
                HostName = settings.SftpHostName,
                PortNo = settings.SftpPort,
                UserName = settings.SftpUserName,
                Password = settings.SftpPass,
                AuthType = settings.AuthType,
                PrivateKeyPath = settings.PrivateKeyPath,
                SftpConnectTimeoutSec = settings.SftpConnectTimeoutSec,
                SftpTransferTimeoutSec = settings.SftpTransferTimeoutSec,
                RecvRemoteDir = settings.RecvRemoteDir,
                RecvLocalDir = settings.RecvLocalDir,
                RecvTargetFileName = settings.RecvZipFileName
            };
        }

        /// <summary>
        /// GET：リモートに recv.zip が存在する場合にローカルへダウンロードする
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="settings">設定値</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryDownloadRecvFile(SftpClient client, SftpRecvPram recvPram)
        {
            try
            {
                this.EnsureLocalDirectory(recvPram.RecvLocalDir);

                var remoteZipPath = CombineRemotePath(recvPram.RecvRemoteDir, recvPram.RecvTargetFileName);
                var localZipPath = Path.Combine(recvPram.RecvLocalDir, recvPram.RecvTargetFileName);

                if (!client.Exists(remoteZipPath))
                {
                    Logger.Info($"[SftpTransferAgent] GET skipped (remote not found). remote='{remoteZipPath}'");
                    return true; // 処理なし＝正常
                }

                Logger.Info($"[SftpTransferAgent] GET start. remote='{remoteZipPath}', local='{localZipPath}'");

                this.GetRemoteFile(client, remoteZipPath, localZipPath);

                Logger.Info($"[SftpTransferAgent] GET success. local='{localZipPath}'");

                // 成功したらリモートファイル削除
                this.TryDeleteRemoteFile(client, remoteZipPath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpTransferAgent] GET failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// GET：ローカルにテンポラリで落としてから置換する（途中失敗で壊れたファイルが残らない）
        /// </summary>
        private void GetRemoteFile(SftpClient client, string remotePath, string localPath)
        {
            var tempPath = localPath + ".tmp";

            // Create: 既存があれば上書き（長さ0にする）
            using (var fs = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                client.DownloadFile(remotePath, fs);
                fs.Flush(true); // 念のため（ディスクにフラッシュ）
            }

            // 置換（既存を消してからmove）
            if (File.Exists(localPath))
                File.Delete(localPath);

            File.Move(tempPath, localPath);
        }

        /// <summary>
        /// リモートファイル削除（失敗しても false にはせずログに残す）
        /// </summary>
        /// /// <param name="client">接続済みSftpClient</param>
        /// <param name="remotePath">設定値</param>
        private void TryDeleteRemoteFile(SftpClient client, string remotePath)
        {
            try
            {
                if (client.Exists(remotePath))
                {
                    client.DeleteFile(remotePath);
                    Logger.Info($"[SftpTransferAgent] RemoteFile deleted. remote='{remotePath}'");
                }
            }
            catch (Exception ex)
            {
                // 「転送自体は成功している」ので致命扱いにしない
                Logger.Warn($"[SftpTransferAgent] RemoteFile delete failed. remote='{remotePath}' ex='{ex.GetType().Name}'");
            }
        }
        #endregion

        #region PUT
        /// <summary>
        /// 設定値からPUT（送信）処理用パラメータを生成する。
        /// </summary>
        /// <param name="settings">設定値。</param>
        /// <returns>送信処理用パラメータ。</returns>
        private SftpSendPram CreateSendPram(CommonSettingValues settings)
        {
            return new SftpSendPram
            {
                HostName = settings.SftpHostName,
                PortNo = settings.SftpPort,
                UserName = settings.SftpUserName,
                Password = settings.SftpPass,
                AuthType = settings.AuthType,
                PrivateKeyPath = settings.PrivateKeyPath,
                SftpConnectTimeoutSec = settings.SftpConnectTimeoutSec,
                SftpTransferTimeoutSec = settings.SftpTransferTimeoutSec,
                SendRemoteDir = settings.SendRemoteDir,
                SendLocalDir = settings.SendLocalDir,
                SendTargetFileName = settings.CompleteFileName
            };
        }

        /// <summary>
        /// PUT：ローカルに download.complete が存在する場合にリモートへアップロードする
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="settings">設定値</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryUploadCompleteFile(SftpClient client, SftpSendPram sendPram)
        {
            try
            {
                this.EnsureLocalDirectory(sendPram.SendLocalDir);

                var localCompletePath = Path.Combine(sendPram.SendLocalDir, sendPram.SendTargetFileName);
                var remoteCompletePath = CombineRemotePath(sendPram.SendRemoteDir, sendPram.SendTargetFileName);

                if (!File.Exists(localCompletePath))
                {
                    Logger.Info($"[SftpTransferAgent] PUT skipped (local not found). local='{localCompletePath}'");
                    return true; // 処理なし＝正常
                }

                if (this.IsFileLockedForRead(localCompletePath))
                {
                    // ロックされていればリトライへ
                    Logger.Warn($"[SftpTransferAgent] PUT postponed (file locked). local='{localCompletePath}'");
                    return false; // Controller側のリトライに乗せる
                }

                Logger.Info($"[SftpTransferAgent] PUT start. local='{localCompletePath}', remote='{remoteCompletePath}'");

                this.PutLocalFile(client, localCompletePath, remoteCompletePath);

                Logger.Info($"[SftpTransferAgent] PUT success. remote='{remoteCompletePath}'");

                // 成功したらローカルファイル削除
                this.TryDeleteLocalFile(localCompletePath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpTransferAgent] PUT failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// ローカルファイルが「読み取り用に排他オープンできない」＝ロック中かどうかを判定する
        /// </summary>
        /// <param name="path">ファイルパス</param>
        /// <returns>True:ロック中 / False:ロック中ではない</returns>
        private bool IsFileLockedForRead(string path)
        {
            try
            {
                // 読み取りを排他で開けるかチェック（開けなければ誰かが掴んでる可能性が高い）
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // 権限や属性でも開けない場合があるのでログで気づけるように「ロック扱い」
                return true;
            }
        }

        /// <summary>
        /// PUT：ローカルファイルをリモートへアップロードする（上書き）
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="localPath">ローカルファイルパス</param>
        /// <param name="remotePath">リモート保存先パス</param>
        private void PutLocalFile(SftpClient client, string localPath, string remotePath)
        {
            if (!File.Exists(localPath))
                throw new FileNotFoundException("Local file not found.", localPath);

            using (var fs = File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                client.UploadFile(fs, remotePath, true);
            }
        }

        /// <summary>
        /// ローカルファイル削除（失敗しても false にはせずログに残す）
        /// </summary>
        /// <param name="localPath">設定値</param>
        private void TryDeleteLocalFile(string localPath)
        {
            try
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                    Logger.Info($"[SftpTransferAgent] LocalFile deleted. local='{localPath}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SftpTransferAgent] LocalFile delete failed. local='{localPath}' ex='{ex.GetType().Name}'");
            }
        }
        #endregion

        /// <summary>
        /// SftpClient を生成する（認証方式・タイムアウト等を設定）
        /// </summary>
        /// <param name="settings">設定値</param>
        /// <returns>SftpClient</returns>
        private SftpClient CreateSftpClient(CommonSettingValues settings)
        {
            var connectionInfo = this.BuildConnectionInfo(settings);

            var client = new SftpClient(connectionInfo)
            {
                OperationTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.SftpTransferTimeoutSec)),
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            // ホストキー検証（現状は設定で持っていないので許可。将来ピン止め推奨）
            client.HostKeyReceived += (s, e) =>
            {
                Logger.Warn("[SftpTransferAgent] HostKeyReceived (NOT validated; consider pinning).");
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