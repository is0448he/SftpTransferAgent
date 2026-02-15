using System;
using System.Security.Cryptography;
using System.Text;

namespace SftpTransferAgent.Common
{
    /// <summary>
    /// DPAPI を用いた文字列暗号化/復号ユーティリティ
    /// </summary>
    internal static class CryptoUtility
    {
        // 追加の紐付け（任意）。同じ値で暗号化/復号する必要がある
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SftpTransferAgent");

        /// <summary>
        /// 平文を DPAPI で暗号化し、Base64文字列にして返す
        /// </summary>
        public static string EncryptToBase64(string plainText, DataProtectionScope scope)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, scope);
            return Convert.ToBase64String(protectedBytes);
        }

        /// <summary>
        /// Base64(DPAPI暗号文)を復号し、平文として返す
        /// </summary>
        public static string DecryptFromBase64(string base64CipherText, DataProtectionScope scope)
        {
            if (string.IsNullOrWhiteSpace(base64CipherText))
                return string.Empty;

            var protectedBytes = Convert.FromBase64String(base64CipherText);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, scope);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}