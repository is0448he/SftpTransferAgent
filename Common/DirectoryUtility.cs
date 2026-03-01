using System;
using System.IO;
using Renci.SshNet;
using SftpTransferAgent.Common.Logging;

namespace SftpTransferAgent.Common
{
    public static class CommonUtility
    {

        /// <summary>
        /// ローカルファイルが「読み取り用に排他オープンできない」＝ロック中かどうかを判定する
        /// </summary>
        /// <param name="path">ファイルパス</param>
        /// <returns>True:ロック中 / False:ロック中ではない</returns>
        public static bool IsFileLockedForRead(string path)
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
        /// リモートファイル削除（失敗しても false にはせずログに残す）
        /// </summary>
        /// /// <param name="client">接続済みSftpClient</param>
        /// <param name="remotePath">リモートファイルパス</param>
        public static void TryDeleteRemoteFile(SftpClient client, string remotePath)
        {
            try
            {
                if (client.Exists(remotePath))
                {
                    client.DeleteFile(remotePath);
                    Logger.Info($"RemoteFile deleted. remote='{remotePath}'");
                }
            }
            catch (Exception ex)
            {
                // 「転送自体は成功している」ので致命扱いにしない
                Logger.Warning($"RemoteFile delete failed. remote='{remotePath}' ex='{ex.GetType().Name}'");
            }
        }

        /// <summary>
        /// ローカルファイル削除（失敗しても false にはせずログに残す）
        /// </summary>
        /// <param name="localPath">ローカルファイルパス</param>
        public static void TryDeleteLocalFile(string localPath)
        {
            try
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                    Logger.Info($"LocalFile deleted. local='{localPath}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"LocalFile delete failed. local='{localPath}' ex='{ex.GetType().Name}'");
            }
        }

        /// <summary>
        /// ローカルディレクトリが存在しない場合は作成する
        /// </summary>
        /// <param name="dir">ディレクトリパス</param>
        public static void EnsureLocalDirectory(string dir)
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
        public static string CombinePath(string dir, string fileName)
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
