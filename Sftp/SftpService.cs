using Renci.SshNet;
using SftpTransferAgent.Common;
using System;
using System.Diagnostics.Eventing.Reader;
using System.IO;

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
        /// <param name="recvPram">受信処理用パラメータ</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryDownloadRecvFile(SftpClient client, SftpRecvPram recvPram)
        {
            try
            {
                this.EnsureLocalDirectory(recvPram.RecvLocalDir);

                var remoteZipPath = this.CombinePath(recvPram.RecvRemoteDir, recvPram.RecvTargetFileName);
                var localZipPath = this.CombinePath(recvPram.RecvLocalDir, recvPram.RecvTargetFileName);

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

        /// <summary>
        /// リモートファイル削除（失敗しても false にはせずログに残す）
        /// </summary>
        /// /// <param name="client">接続済みSftpClient</param>
        /// <param name="remotePath">リモートファイルパス</param>
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
        /// <param name="sendPram">送信処理用パラメータ</param>
        /// <returns>True:正常 / False:異常</returns>
        private bool TryUploadCompleteFile(SftpClient client, SftpSendPram sendPram)
        {
            try
            {
                this.EnsureLocalDirectory(sendPram.SendLocalDir);

                var localCompletePath = this.CombinePath(sendPram.SendLocalDir, sendPram.SendTargetFileName);
                var remoteCompletePath = this.CombinePath(sendPram.SendRemoteDir, sendPram.SendTargetFileName);

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
        /// <param name="localPath">ローカルファイルパス</param>
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
        /// ディレクトリとファイル名を結合してパス文字列を生成する（ローカル/リモート共通）。
        /// 設定値は極力そのまま尊重し、結合点の区切り重複（末尾の / や \）だけを吸収する。
        /// </summary>
        /// <param name="dir">ディレクトリ（例: "C:\SftpAgent\recv", "/recv/", "/C:/SftpServer"）</param>
        /// <param name="fileName">ファイル名（例: "recv.zip"）</param>
        /// <returns>結合済みパス</returns>
        private string CombinePath(string dir, string fileName)
        {
            dir = (dir ?? string.Empty).Trim();
            fileName = (fileName ?? string.Empty).Trim();

            if (dir.Length == 0)
            {
                return fileName;
            }

            if (fileName.Length == 0)
            {
                return dir;
            }
            

            // 区切りは dir 側に寄せる（dir に '\' が含まれていて '/' が無ければ '\'、それ以外は '/'）
            var sep = (dir.IndexOf('\\') >= 0 && dir.IndexOf('/') < 0) ? '\\' : '/';

            // 結合点のみ整形（中身の置換や正規化はしない）
            var d = dir.TrimEnd('/', '\\');

            // dir が "/" や "\" のみの場合に TrimEnd で空になる → ルート扱い
            if (d.Length == 0) return sep + fileName;

            return d + sep + fileName;
        }
    }
}