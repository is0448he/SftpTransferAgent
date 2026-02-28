using System;
using System.Threading;
using System.Windows.Forms;
using SftpTransferAgent.Common;
using SftpTransferAgent.Common.Logging;
using SftpTransferAgent.Sftp;

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
        /// Stop時に待機を解除するためのシグナル
        /// </summary>
        private readonly ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);

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
                            Logger.Warning($"[SftpTransferAgent] Run SftpTransferAgent's while loop end.");
                            break;
                        }

                        this.ExecuteTransfer();

                        // 待機(ポーリング間隔)：
                        // Stop() が呼ばれて _stopSignal.Set() されると、timeoutを待たずに即復帰する
                        this._stopSignal.Wait(CommonSettingValues.Current.PollingIntervalMillSec);
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
            Logger.Warning($"[SftpTransferAgent] Stop method called.");
            this._onStopCalled = true;

            // 待機を即解除
            this._stopSignal.Set();
        }

        // <summary>
        /// 送受信処理（1回分）をリトライ付きで実行
        /// </summary>
        private void ExecuteTransfer()
        {
            int executeCount = 0;

            while (true)
            {
                if (this._onStopCalled)
                {
                    Logger.Warning("[SftpTransferAgent] Stop requested before transfer.");
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
                    Logger.Warning($"[SftpTransferAgent] ExecuteTransfer returned false.リトライ;{executeCount}回目");
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
                
                // 待機(リトライ間隔)
                this._stopSignal.Wait(CommonSettingValues.Current.RetryIntervalMilliSec);
            }
        }
    }
}