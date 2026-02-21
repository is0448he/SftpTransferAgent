using SftpTransferAgent.Common;

namespace SftpTransferAgent.Sftp.Factorys
{
    /// <summary>
    /// SFTP処理用パラメータ（接続/受信/送信）を生成するファクトリ
    /// 共通項目の設定を一箇所に集約し、重複と設定漏れを防ぐ
    /// </summary>
    internal static class SftpPramFactory
    {
        /// <summary>
        /// 設定値から接続情報パラメータを生成する
        /// </summary>
        /// <remarks>
        /// 接続に必要な共通項目のみを設定
        /// </remarks>
        /// <returns>接続情報パラメータ</returns>
        public static SftpConnectionPram CreateConnectionPram()
        {
            var s = CommonSettingValues.Current;
            return CreateWithCommon<SftpConnectionPram>(s);
        }

        /// <summary>
        /// 設定値からGET(受信)処理用パラメータを生成する
        /// </summary>
        /// <returns>受信処理用パラメータ</returns>
        public static SftpRecvPram CreateRecvPram()
        {
            var s = CommonSettingValues.Current;

            var p = CreateWithCommon<SftpRecvPram>(s);
            p.RecvRemoteDir = s.RecvRemoteDir;
            p.RecvLocalDir = s.RecvLocalDir;
            p.RecvTargetFileName = s.RecvZipFileName;
            return p;
        }

        /// <summary>
        /// 設定値からPUT(送信)処理用パラメータを生成する
        /// </summary>
        /// <returns>送信処理用パラメータ</returns>
        public static SftpSendPram CreateSendPram()
        {
            var s = CommonSettingValues.Current;

            var p = CreateWithCommon<SftpSendPram>(s);
            p.SendRemoteDir = s.SendRemoteDir;
            p.SendLocalDir = s.SendLocalDir;
            p.SendTargetFileName = s.CompleteFileName;
            return p;
        }

        /// <summary>
        /// 共通設定を設定したパラメータを生成する
        /// </summary>
        /// <typeparam name="T">生成するパラメータ型（<see cref="SftpPramBase"/> 派生）</typeparam>
        /// <param name="s">設定値スナップショット</param>
        /// <returns>
        /// 共通項目（HostName/PortNo/UserName/Password/AuthType/PrivateKeyPath/SftpConnectTimeoutSec/SftpTransferTimeoutSec）が設定済みの <typeparamref name="T"/> インスタンス
        /// </returns>
        private static T CreateWithCommon<T>(CommonSettingValues s)
            where T : SftpPramBase, new()
        {
            return new T
            {
                HostName = s.SftpHostName,
                PortNo = s.SftpPort,
                UserName = s.SftpUserName,
                Password = s.SftpPass,
                AuthType = s.AuthType,
                PrivateKeyPath = s.PrivateKeyPath,
                SftpConnectTimeoutSec = s.SftpConnectTimeoutSec,
                SftpTransferTimeoutSec = s.SftpTransferTimeoutSec
            };
        }
    }
}
