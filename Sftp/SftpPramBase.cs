namespace SftpTransferAgent.Sftp
{
    /// <summary>
    /// SFTPパラメータ基底クラス（接続情報）
    /// </summary>
    public abstract class SftpPramBase
    {
        /// <summary>
        /// SFTPサーバホスト名
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// SFTPポート番号
        /// </summary>
        public int PortNo { get; set; }

        /// <summary>
        /// SFTPユーザー名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// SFTPパスワード
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 認証方式（Password / PrivateKey）
        /// </summary>
        public string AuthType { get; set; }

        /// <summary>
        /// 秘密鍵ファイルパス
        /// </summary>
        public string PrivateKeyPath { get; set; }

        /// <summary>
        /// 接続タイムアウト（秒）
        /// </summary>
        public int SftpConnectTimeoutSec { get; set; }

        /// <summary>
        /// 転送タイムアウト（秒）
        /// </summary>
        public int SftpTransferTimeoutSec { get; set; }
    }
}
