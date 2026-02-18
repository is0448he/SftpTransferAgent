namespace SftpTransferAgent.Sftp
{
    /// <summary>
    /// GET（受信）処理用のパラメータ
    /// </summary>
    public sealed class SftpRecvPram : SftpPramBase
    {
        /// <summary>
        /// 受信元ディレクトリパス（リモート）
        /// </summary>
        public string RecvRemoteDir { get; set; }

        /// <summary>
        /// 受信先ディレクトリパス（ローカル）
        /// </summary>
        public string RecvLocalDir { get; set; }

        /// <summary>
        /// 受信対象の連携ファイル名
        /// </summary>
        public string RecvTargetFileName { get; set; }
    }
}
