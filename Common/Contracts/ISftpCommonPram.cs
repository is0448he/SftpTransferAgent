using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SftpTransferAgent.Common.Contracts
{
    /// <summary>
    /// SFTP接続インタフェース
    /// 受信/送信/接続パラメータで共通利用する
    /// </summary>
    public interface ISftpCommonPram
    {
        /// <summary>
        /// SFTPサーバホスト名
        /// </summary>
        string HostName { get; set; }

        /// <summary>
        /// SFTPポート番号
        /// </summary>
        int PortNo { get; set; }

        /// <summary>
        /// SFTPユーザー名
        /// </summary>
        string UserName { get; set; }

        /// <summary>
        /// SFTPパスワード
        /// </summary>
        string Password { get; set; }

        /// <summary>
        /// 認証方式（Password / PrivateKey）
        /// </summary>
        string AuthType { get; set; }

        /// <summary>
        /// 秘密鍵ファイルパス
        /// </summary>
        string PrivateKeyPath { get; set; }

        /// <summary>
        /// 接続タイムアウト（秒）
        /// </summary>
        int SftpConnectTimeoutSec { get; set; }

        /// <summary>
        /// 転送タイムアウト（秒）
        /// </summary>
        int SftpTransferTimeoutSec { get; set; }
    }
}
