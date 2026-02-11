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
        private readonly CommonSettingValues _settings;
        private readonly SftpService _sftpService;

        /// <summary>システム停止フラグ</summary>
        private volatile bool _onStopCalled = false;

        public Controller()
            : this(CommonSettingValues.Load(), new SftpService())
        {
        }

        // テスト・拡張のため注入可能にしておく
        internal Controller(CommonSettingValues settings, SftpService sftpService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _sftpService = sftpService ?? throw new ArgumentNullException(nameof(sftpService));
        }

        /// <summary>
        /// 処理開始
        /// </summary>
        public void Run()
        {
            Logger.Info($"[SftpTransferAgent] Run method called.");

            try
            {
                if (_settings.PollingEnabled)
                {
                    while (true)
                    {
                        // システム停止要求があった場合、処理を終了する
                        if (this._onStopCalled == true)
                        {
                            Logger.Warn($"Run SftpTransferAgent's while loop end.");
                            break;
                        }

                        this.ExecuteTransfer();

                        // 待機(ポーリング間隔(s))
                        Thread.Sleep(_settings.PollingIntervalMillSec);
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
                    Logger.Info($"[SftpTransferAgent] ExecuteTransfer start. attempt={executeCount}");

                    // ★ SFTPの実装詳細は知らない。結果だけ受け取る。
                    bool ok = _sftpService.Execute(_settings);

                    if (ok)
                    {
                        Logger.Info("[SftpTransferAgent] ExecuteTransfer success.");
                        return;
                    }

                    // false = 失敗扱い（リトライ対象）
                    Logger.Warn("[SftpTransferAgent] ExecuteTransfer returned false.");
                }
                catch (Exception ex)
                {
                    // 例外も失敗扱い（リトライ対象）
                    Logger.Error($"[SftpTransferAgent] ExecuteTransfer exception. attempt={executeCount}", ex);
                }

                // リトライ判定
                if (executeCount > _settings.RetryMaxCount)
                {
                    Logger.Error($"[SftpTransferAgent] Retry exceeded. max={_settings.RetryMaxCount}", null);
                    return;
                }

                if (_settings.RetryIntervalMilliSec > 0)
                {
                    // 待機(ポーリング間隔(s))
                    Thread.Sleep(_settings.RetryIntervalMilliSec);
                }
            }
        }
    }
}