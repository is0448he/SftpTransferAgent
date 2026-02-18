namespace SftpTransferAgent.Sftp
{
    /// <summary>
    /// SFTP送信(PUT)パラメータ
    /// </summary>
    public sealed class SftpSendPram : SftpPramBase
    {
        /// <summary>
        /// 送信先ディレクトリパス（リモート）
        /// </summary>
        public string SendRemoteDir { get; set; }

        /// <summary>
        /// 送信元ディレクトリパス（ローカル）
        /// </summary>
        public string SendLocalDir { get; set; }

        /// <summary>
        /// 送信対象のディレクトリファイル名
        /// </summary>
        public string SendTargetFileName { get; set; }
    }
}
