<#
  目的：
    指定フォルダ配下にあるログファイル（<BaseName>-YYYY-M-D.log 形式）について、
    保持期間を過ぎたものを完全削除する。

  例：
    SftpTransferAgent-2026-2-26.log

  仕様：
    - 判定は「ファイル名に含まれる日付」を使用する
    - 削除は完全削除（退避・バックアップ・移動は行わない）
    - 対象は指定フォルダ直下のみ（サブフォルダは対象外）
    - 最後に削除件数を出力する
#>

# =========================
# 設定（ここだけ編集）
# =========================
# ログを格納しているフォルダ（末尾に \ は付けても付けなくても可）
$LogDirectory  = "C:\ProgramData\YourCo\SftpTransferAgent\Logs"

# ログファイル名のプレフィックス（例：SftpTransferAgent）
$BaseName      = "SftpTransferAgent"

# 保持日数（例：30 の場合、今日から30日より前の日付のログを削除）
$RetentionDays = 30
# =========================

$ErrorActionPreference = "Stop"

# 指定フォルダが存在しない場合はエラーにする
if (-not (Test-Path -LiteralPath $LogDirectory)) {
    throw "ログフォルダが見つかりません: $LogDirectory"
}

# 削除対象の基準日：
# ファイル名の日付がこの日付より「前」のものを削除する
$cutoff = (Get-Date).Date.AddDays(-$RetentionDays)

# 対象ファイル：<BaseName>-*.log
$filter = "$BaseName-*.log"

# ファイル名が <BaseName>-YYYY-M-D.log 形式か判定するための正規表現
$baseEsc = [regex]::Escape($BaseName)
$pattern = "^{0}-(\d{{4}})-(\d{{1,2}})-(\d{{1,2}})\.log$" -f $baseEsc
$regex = [regex]::new($pattern)

# 削除件数
$deleteCount = 0

# 指定フォルダ直下のファイルを取得（サブフォルダは対象外）
$files = Get-ChildItem -LiteralPath $LogDirectory -File -Filter $filter

foreach ($f in $files) {

    # ファイル名が想定形式でなければ対象外
    $m = $regex.Match($f.Name)
    if (-not $m.Success) { continue }

    # ファイル名から日付（YYYY-M-D）を取り出して DateTime に変換
    try {
        $y  = [int]$m.Groups[1].Value
        $mo = [int]$m.Groups[2].Value
        $d  = [int]$m.Groups[3].Value
        $fileDate = [datetime]::new($y, $mo, $d)
    }
    catch {
        # 日付として不正なら対象外
        continue
    }

    # 保持期間を過ぎている（基準日より前）なら削除
    if ($fileDate -lt $cutoff) {
        Remove-Item -LiteralPath $f.FullName -Force
        $deleteCount++
    }
}

# 最後に削除件数のみ出力
Write-Host "削除件数: $deleteCount"

exit 0