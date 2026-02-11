using SftpTransferAgent.Common;

namespace SftpTransferAgent.Sftp
{
    /// <summary>
    /// SFTP通信サービス
    /// </summary>
    public class SftpService
    {
        /// <summary>
        /// ファイル送受信処理を実行する
        /// </summary>
        /// <param name="settings"></param>
        /// <returns>True:正常/False：異常</returns>
        public bool Execute(CommonSettingValues settings)
        {
            // TODO: SSH.NET を使った接続／送受信処理を実装
            Logger.Info("[SftpService] Execute called (stub).");
            return true;
        }
    }
}