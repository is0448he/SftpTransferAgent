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
        private readonly SftpConnectionPram _connectionPram;
        private readonly SftpRecvPram _recvPram;
        private readonly SftpSendPram _sendPram;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SftpService()
        {
            try
            {
                _connectionPram = CreateConnectionPram();
                _recvPram = CreateRecvPram();
                _sendPram = CreateSendPram();
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpTransferAgent] 設定ファイルからのパラメータ取得に失敗しました。", ex);
                throw;
            }
        }

        /// <summary>
        /// ファイル送受信処理を実行する（1回分）
        /// - GET: リモート(RecvRemoteDir/RecvZipFileName) -> ローカル(RecvLocalDir/RecvZipFileName)
        /// - PUT: ローカル(SendLocalDir/CompleteFileName) -> リモート(SendRemoteDir/CompleteFileName)
        /// </summary>
        /// <returns>True:正常 / False:異常（Controller側でリトライ対象）</returns>
        public bool Execute()
        {
            try
            {
                using (var client = CreateSftpClient(_connectionPram))
                {
                    client.Connect();

                    // GET（recv.zip）
                    if (!TryDownloadRecvFile(client, _recvPram))
                        return false;

                    // PUT（download.complete）
                    if (!TryUploadCompleteFile(client, _sendPram))
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

        #region CreatePram
        /// <summary>
        /// 設定値から接続情報パラメータを生成する
        /// </summary>
        /// <returns>受信処理用パラメータ</returns>
        private SftpConnectionPram CreateConnectionPram()
        {
            return new SftpConnectionPram
            {
                HostName = CommonSettingValues.Current.SftpHostName,
                PortNo = CommonSettingValues.Current.SftpPort,
                UserName = CommonSettingValues.Current.SftpUserName,
                Password = CommonSettingValues.Current.SftpPass,
                AuthType = CommonSettingValues.Current.AuthType,
                PrivateKeyPath = CommonSettingValues.Current.PrivateKeyPath,
                SftpConnectTimeoutSec = CommonSettingValues.Current.SftpConnectTimeoutSec,
                SftpTransferTimeoutSec = CommonSettingValues.Current.SftpTransferTimeoutSec
            };
        }

        /// <summary>
        /// 設定値からGET（受信）処理用パラメータを生成する。
        /// </summary>
        /// <returns>受信処理用パラメータ</returns>
        private SftpRecvPram CreateRecvPram()
        {
            return new SftpRecvPram
            {
                HostName = CommonSettingValues.Current.SftpHostName,
                PortNo = CommonSettingValues.Current.SftpPort,
                UserName = CommonSettingValues.Current.SftpUserName,
                Password = CommonSettingValues.Current.SftpPass,
                AuthType = CommonSettingValues.Current.AuthType,
                PrivateKeyPath = CommonSettingValues.Current.PrivateKeyPath,
                SftpConnectTimeoutSec = CommonSettingValues.Current.SftpConnectTimeoutSec,
                SftpTransferTimeoutSec = CommonSettingValues.Current.SftpTransferTimeoutSec,
                RecvRemoteDir = CommonSettingValues.Current.RecvRemoteDir,
                RecvLocalDir = CommonSettingValues.Current.RecvLocalDir,
                RecvTargetFileName = CommonSettingValues.Current.RecvZipFileName
            };
        }

        /// <summary>
        /// 設定値からPUT（送信）処理用パラメータを生成する。
        /// </summary>
        /// <returns>送信処理用パラメータ</returns>
        private SftpSendPram CreateSendPram()
        {
            return new SftpSendPram
            {
                HostName = CommonSettingValues.Current.SftpHostName,
                PortNo = CommonSettingValues.Current.SftpPort,
                UserName = CommonSettingValues.Current.SftpUserName,
                Password = CommonSettingValues.Current.SftpPass,
                AuthType = CommonSettingValues.Current.AuthType,
                PrivateKeyPath = CommonSettingValues.Current.PrivateKeyPath,
                SftpConnectTimeoutSec = CommonSettingValues.Current.SftpConnectTimeoutSec,
                SftpTransferTimeoutSec = CommonSettingValues.Current.SftpTransferTimeoutSec,
                SendRemoteDir = CommonSettingValues.Current.SendRemoteDir,
                SendLocalDir = CommonSettingValues.Current.SendLocalDir,
                SendTargetFileName = CommonSettingValues.Current.CompleteFileName
            };
        }
        #endregion

        #region GET
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
        private SftpClient CreateSftpClient(SftpConnectionPram connectionPram)
        {
            var connectionInfo = this.BuildConnectionInfo(connectionPram);

            var client = new SftpClient(connectionInfo)
            {
                OperationTimeout = TimeSpan.FromSeconds(Math.Max(1, connectionPram.SftpTransferTimeoutSec)),
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
        /// <param name="connectionPram">設定値</param>
        /// <returns>ConnectionInfo</returns>
        private ConnectionInfo BuildConnectionInfo(SftpConnectionPram connectionPram)
        {
            var port = connectionPram.PortNo <= 0 ? 22 : connectionPram.PortNo;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, connectionPram.SftpConnectTimeoutSec));

            if (string.Equals(connectionPram.AuthType, "PrivateKey", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(connectionPram.PrivateKeyPath))
                    throw new InvalidOperationException("PrivateKeyPath is empty for PrivateKey auth.");
                if (!File.Exists(connectionPram.PrivateKeyPath))
                    throw new FileNotFoundException("PrivateKey file not found.", connectionPram.PrivateKeyPath);

                // パスフレーズが必要な鍵の場合は SftpPass を流用（必要なら設定キー分離）
                var keyFile = string.IsNullOrWhiteSpace(connectionPram.Password)
                    ? new PrivateKeyFile(connectionPram.PrivateKeyPath)
                    : new PrivateKeyFile(connectionPram.PrivateKeyPath, connectionPram.Password);

                var auth = new PrivateKeyAuthenticationMethod(connectionPram.UserName, keyFile);

                return new ConnectionInfo(connectionPram.UserName, port, connectionPram.UserName, auth)
                {
                    Timeout = timeout
                };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(connectionPram.Password))
                    throw new InvalidOperationException("SftpPass is empty for Password auth.");

                var auth = new PasswordAuthenticationMethod(connectionPram.UserName, connectionPram.Password);

                return new ConnectionInfo(connectionPram.HostName, port, connectionPram.UserName, auth)
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