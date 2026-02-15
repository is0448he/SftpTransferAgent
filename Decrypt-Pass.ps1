<#
.SYNOPSIS
  SftpTransferAgent 用：SftpPassEnc（DPAPI暗号化Base64）を生成するスクリプト

.DESCRIPTION
  App.config に SFTP パスワード（SftpPass）を平文で置かず、暗号化済み文字列（SftpPassEnc）として保存するためのスクリプトです。

  本スクリプトは、あなたの CryptoUtility.cs と完全に一致するように固定しています：
    - Entropy : "SftpTransferAgent"（UTF-8バイト列）
    - Scope   : DataProtectionScope.LocalMachine

  重要：
    - LocalMachine スコープのDPAPI暗号化は「同一マシン上」で復号できます。
      したがって、暗号化文字列（SftpPassEnc）は「実際に動かすサーバ上で生成する」運用を推奨します。
    - 生成した Base64 は App.config の SftpPassEnc に貼り付け、SftpPass（平文）は空にするのが安全です。

.OUTPUTS
  標準出力に SftpPassEnc（Base64）を出力します。

.USAGE
  1) この内容を例：SftpPassEnc-Encrypt.ps1 として保存
  2) PowerShellで実行
     PS> .\SftpPassEnc-Encrypt.ps1
  3) 出力された Base64 を App.config の <add key="SftpPassEnc" value="..."/> に貼る

.NOTES
  - 復号（平文表示）は漏洩リスクが上がるため、このスクリプトには含めていません。
  - CryptoUtility の Entropy や Scope を将来変更した場合、本スクリプトも同様に変更が必要です。

#>

# 厳格モード（未定義変数などをエラーにして事故を防ぐ）
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"  # 途中で失敗したら即終了（曖昧な状態で継続しない）

# DPAPI（ProtectedData）を使うため System.Security を読み込む
Add-Type -AssemblyName System.Security

# ------------------------------
# CryptoUtility.cs と一致させる固定値
# ------------------------------

# CryptoUtility.cs:
# private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SftpTransferAgent");
$entropyBytes = [Text.Encoding]::UTF8.GetBytes("SftpTransferAgent")

# CryptoUtility で渡す scope は運用の簡便さを優先して LocalMachine を固定
#（同一マシン上であれば復号可能。タスクスケジューラ運用で扱いやすい）
$scope = [Security.Cryptography.DataProtectionScope]::LocalMachine

# ------------------------------
# 1) パスワード入力（SecureString）
# ------------------------------
# Read-Host -AsSecureString を使うことで、入力内容が画面に表示されない
Write-Host "SFTP Password を入力してください（画面には表示されません）"
$secure = Read-Host "Password" -AsSecureString

# ------------------------------
# 2) SecureString → 平文文字列へ変換（最小範囲）
# ------------------------------
# DPAPIで暗号化するにはバイト配列が必要なため、一時的に平文へ変換する。
# 変換には BSTR（メモリ領域）を使い、最後にゼロ化して解放する（漏洩リスク低減）。
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
}
finally {
    # 平文が残らないようにメモリをゼロ化して解放
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

if ([string]::IsNullOrWhiteSpace($plain)) {
    throw "Password is empty."
}

# ------------------------------
# 3) 平文 → UTF-8 バイト配列へ変換
# ------------------------------
# CryptoUtility と同じく UTF-8 を使用する（Encoding.UTF8.GetBytes）
$plainBytes = [Text.Encoding]::UTF8.GetBytes($plain)

# ------------------------------
# 4) DPAPIで暗号化（Protect）
# ------------------------------
# ProtectedData.Protect(平文bytes, Entropy, Scope)
# CryptoUtility.EncryptToBase64 と同じアルゴリズム／同じEntropy／同じScopeになる。
$protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
    $plainBytes,
    $entropyBytes,
    $scope
)

# ------------------------------
# 5) Base64文字列へ変換（App.configへ貼れる形）
# ------------------------------
# Convert.ToBase64String で文字列化して、設定ファイルに貼り付けやすくする。
$base64 = [Convert]::ToBase64String($protectedBytes)

# ------------------------------
# 6) 出力
# ------------------------------
# ここで出る Base64 を App.config の SftpPassEnc に貼り付ける。
Write-Host ""
Write-Host "SftpPassEnc（Base64）:"
Write-Output $base64

Write-Host ""
Write-Host "App.config 設定例（平文SftpPassは空推奨）:"
Write-Host "<add key=""SftpPassEnc"" value=""$base64"" />"
Write-Host "<add key=""SftpPass"" value="""" />"