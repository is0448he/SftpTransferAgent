using SftpTransferAgent.Common;
using SftpTransferAgent.Sftp;
using System;
using System.Threading;
using System.Windows.Forms;

namespace SftpTransferAgent
{
    /// <summary>
    /// コントローラー
    /// </summary>
    public class Controller
    {
        /// <summary>
        /// SFTPサービス
        /// </summary>
        private readonly SftpService _sftpService;

        /// <summary>
        /// システム停止フラグ
        /// </summary>
        private volatile bool _onStopCalled = false;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Controller()
        {
            try
            {
                _sftpService = new SftpService();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 処理開始
        /// </summary>
        public void Run()
        {
            Logger.Info($"[SftpTransferAgent] Run method called.");

            try
            {
                if (CommonSettingValues.Current.PollingEnabled)
                {
                    while (true)
                    {
                        // システム停止要求があった場合、処理を終了する
                        if (this._onStopCalled == true)
                        {
                            Logger.Warn($"[SftpTransferAgent] Run SftpTransferAgent's while loop end.");
                            break;
                        }

                        this.ExecuteTransfer();

                        // 待機(ポーリング間隔(s))
                        Thread.Sleep(CommonSettingValues.Current.PollingIntervalMillSec);
                    }
                }
                else
                {
                    Logger.Info("[SftpTransferAgent] Polling disabled. Execute once and exit.");
                    this.ExecuteTransfer();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[SftpTransferAgent] Unhandled exception in Run.", ex);
            }
            finally
            {
                Logger.Info("[SftpTransferAgent] Run method end.");
                Application.Exit();
            }
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        public void Stop()
        {
            Logger.Warn($"[SftpTransferAgent] Stop method called.");
            this._onStopCalled = true;
        }

        // <summary>
        /// 送受信処理（1回分）をリトライ付きで実行
        /// </summary>
        private void ExecuteTransfer()
        {
            int executeCount = 0;

            while (true)
            {
                if (_onStopCalled)
                {
                    Logger.Warn("[SftpTransferAgent] Stop requested before transfer.");
                    return;
                }

                try
                {
                    executeCount++;
                    Logger.Info($"[SftpTransferAgent] ExecuteTransfer start.");

                    // 結果だけ受け取る
                    bool ok = _sftpService.Execute();

                    if (ok)
                    {
                        Logger.Info("[SftpTransferAgent] ExecuteTransfer success.");
                        return;
                    }

                    // false = 失敗扱い（リトライ対象）
                    Logger.Warn($"[SftpTransferAgent] ExecuteTransfer returned false.リトライ;{executeCount}回目");
                }
                catch (Exception ex)
                {
                    // 例外も失敗扱い（リトライ対象）
                    Logger.Error($"[SftpTransferAgent] ExecuteTransfer exception. リトライ;{executeCount}回目", ex);
                }

                // リトライ判定
                if (executeCount >= CommonSettingValues.Current.RetryMaxCount)
                {
                    Logger.Error($"[SftpTransferAgent] Retry exceeded. max={CommonSettingValues.Current.RetryMaxCount}", null);
                    return;
                }

                // 待機(ポーリング間隔(s))
                Thread.Sleep(CommonSettingValues.Current.RetryIntervalMilliSec);
            }
        }
    }
}