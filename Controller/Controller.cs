using SftpTransferAgent.Common;
using System.Threading;
using System.Windows.Forms;

namespace SftpTransferAgent
{
    /// <summary>
    /// コントローラー
    /// </summary>
    public class Controller
    {
        private int _intervalMilliseconds = 1000;

        /// <summary>システム停止フラグ</summary>
        private bool _onStopCalled = false;

        /// <summary>
        /// 処理開始
        /// </summary>
        public void Run()
        {
            Logger.Info($"[SftpTransferAgent] Run method called.");

            while (true)
            {
                // システム停止要求があった場合、処理を終了する
                if (this._onStopCalled == true)
                {
                    Logger.Warn($"[SftpTransferAgent] Run method's while loop end.");
                    break;
                }

                Thread.Sleep(_intervalMilliseconds);
            }

            Logger.Info($"[SftpTransferAgent] Run method end.");

            Application.Exit();
        }

        /// <summary>
        /// 終了処理
        /// </summary>
        public void Stop()
        {
            Logger.Warn($"[SftpTransferAgent] Stop method called.");
            this._onStopCalled = true;
        }
    }
}