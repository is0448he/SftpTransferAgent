using Renci.SshNet;
using SftpTransferAgent.Common;
using System;
using System.IO;
using SftpTransferAgent.Sftp.Factorys;

namespace SftpTransferAgent.Sftp
{
    /// <summary>
    /// SFTP通信サービス（外部I/F）
    /// </summary>
    public class SftpService
    {
        /// <summary>
        /// SFTP接続情報パラメータ
        /// </summary>
        private readonly SftpConnectionPram _connectionPram;

        /// <summary>
        /// SFTP受信パラメータ
        /// </summary>
        private readonly SftpRecvPram _recvPram;

        /// <summary>
        /// SFTP送信パラメータ
        /// </summary>
        private readonly SftpSendPram _sendPram;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SftpService()
        {
            try
            {
                _connectionPram = SftpPramFactory.CreateConnectionPram();
                _recvPram = SftpPramFactory.CreateRecvPram();
                _sendPram = SftpPramFactory.CreateSendPram();
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

                    // GET
                    if (!TryDownloadRecvFile(client, _recvPram))
                        return false;

                    // PUT
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

        #region GET
        /// <summary>
        /// GET：リモートに recv.zip が存在する場合にローカルへダウンロードする
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="recvPram">受信処理用パラメータ</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryDownloadRecvFile(SftpClient client, SftpRecvPram recvPram)
        {
            try
            {
                CommonUtility.EnsureLocalDirectory(recvPram.RecvLocalDir);

                var remoteZipPath = CommonUtility.CombinePath(recvPram.RecvRemoteDir, recvPram.RecvTargetFileName);
                var localZipPath = CommonUtility.CombinePath(recvPram.RecvLocalDir, recvPram.RecvTargetFileName);

                if (!client.Exists(remoteZipPath))
                {
                    Logger.Info($"[SftpTransferAgent] GET skipped (remote not found). remote='{remoteZipPath}'");
                    return true; // 処理なし＝正常
                }

                Logger.Info($"[SftpTransferAgent] GET start. remote='{remoteZipPath}', local='{localZipPath}'");

                this.GetRemoteFile(client, remoteZipPath, localZipPath);

                Logger.Info($"[SftpTransferAgent] GET success. local='{localZipPath}'");

                // 成功したらリモートファイル削除
                CommonUtility.TryDeleteRemoteFile(client, remoteZipPath);

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
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="remotePath">リモートファイルパス</param>
        /// <param name="localPath">ローカルファイルパス</param>
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
        #endregion

        #region PUT
        /// <summary>
        /// PUT：ローカルに download.complete が存在する場合にリモートへアップロードする
        /// </summary>
        /// <param name="client">接続済みSftpClient</param>
        /// <param name="sendPram">送信処理用パラメータ</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryUploadCompleteFile(SftpClient client, SftpSendPram sendPram)
        {
            try
            {
                CommonUtility.EnsureLocalDirectory(sendPram.SendLocalDir);

                var localCompletePath = CommonUtility.CombinePath(sendPram.SendLocalDir, sendPram.SendTargetFileName);
                var remoteCompletePath = CommonUtility.CombinePath(sendPram.SendRemoteDir, sendPram.SendTargetFileName);

                if (!File.Exists(localCompletePath))
                {
                    Logger.Info($"[SftpTransferAgent] PUT skipped (local not found). local='{localCompletePath}'");
                    return true; // 処理なし＝正常
                }

                if (CommonUtility.IsFileLockedForRead(localCompletePath))
                {
                    // ロックされていればリトライへ
                    Logger.Warn($"[SftpTransferAgent] PUT postponed (file locked). local='{localCompletePath}'");
                    return false; // Controller側のリトライに乗せる
                }

                Logger.Info($"[SftpTransferAgent] PUT start. local='{localCompletePath}', remote='{remoteCompletePath}'");

                this.PutLocalFile(client, localCompletePath, remoteCompletePath);

                Logger.Info($"[SftpTransferAgent] PUT success. remote='{remoteCompletePath}'");

                // 成功したらローカルファイル削除
                CommonUtility.TryDeleteLocalFile(localCompletePath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpTransferAgent] PUT failed.", ex);
                return false;
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
        #endregion

        /// <summary>
        /// SftpClient を生成する（認証方式・タイムアウト等を設定）
        /// </summary>
        /// <param name="connectionPram">SFTP接続情報パラメータ</param>
        /// <returns>SftpClient</returns>
        private SftpClient CreateSftpClient(SftpConnectionPram connectionPram)
        {
            var connectionInfo = this.BuildConnectionInfo(connectionPram);

            var client = new SftpClient(connectionInfo);
            client.OperationTimeout = TimeSpan.FromSeconds(Math.Max(1, connectionPram.SftpTransferTimeoutSec));
            client.KeepAliveInterval = TimeSpan.FromSeconds(30);

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
        /// <param name="connectionPram">SFTP接続情報パラメータ</param>
        /// <returns>ConnectionInfo</returns>
        private ConnectionInfo BuildConnectionInfo(SftpConnectionPram connectionPram)
        {
            if (connectionPram == null) throw new ArgumentNullException(nameof(connectionPram));
            
            var port = connectionPram.PortNo <= 0 ? 22 : connectionPram.PortNo;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, connectionPram.SftpConnectTimeoutSec));

            if (string.IsNullOrWhiteSpace(connectionPram.HostName))
                throw new InvalidOperationException("SftpHostName is empty.");
            if (string.IsNullOrWhiteSpace(connectionPram.UserName))
                throw new InvalidOperationException("SftpUserName is empty.");

            // AuthType を正規化
            var authType = (connectionPram.AuthType ?? string.Empty).Trim();

            switch (authType.ToUpperInvariant())
            {
                case "PRIVATEKEY":
                    {
                        if (string.IsNullOrWhiteSpace(connectionPram.PrivateKeyPath))
                            throw new InvalidOperationException("PrivateKeyPath is empty for PrivateKey auth.");
                        if (!File.Exists(connectionPram.PrivateKeyPath))
                            throw new FileNotFoundException("PrivateKey file not found.", connectionPram.PrivateKeyPath);

                        var keyFile = new PrivateKeyFile(connectionPram.PrivateKeyPath);

                        // 接続の途中でサーバが投げてくるチャレンジに対して、SSH.NETが内部で署名して応答する
                        var auth = new PrivateKeyAuthenticationMethod(connectionPram.UserName, keyFile);

                        var conn = new ConnectionInfo(connectionPram.HostName, port, connectionPram.UserName, auth);
                        conn.Timeout = timeout;

                        return conn;
                    }

                case "PASSWORD":
                    {
                        // AuthType未指定なら PASSWORD 扱いにしたい場合は "" をここに入れる
                        if (string.IsNullOrWhiteSpace(connectionPram.Password))
                            throw new InvalidOperationException("SftpPass is empty for Password auth.");

                        var auth = new PasswordAuthenticationMethod(connectionPram.UserName, connectionPram.Password);

                        var conn = new ConnectionInfo(connectionPram.HostName, port, connectionPram.UserName, auth);
                        conn.Timeout = timeout;

                        return conn;
                    }

                default:
                    throw new InvalidOperationException($"Unknown AuthType: '{connectionPram.AuthType}'.");
            }
        }
    }
}