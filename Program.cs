using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SftpTransferAgent.Common.Logging;

namespace SftpTransferAgent
{
    internal static class Program
    {
        /// <summary>コントローラー</summary>
        private static Controller _controller;

        /// <summary>タスクトレイアイコン（GC回収・終了時のDispose対策）</summary>
        private static NotifyIcon _notifyIcon;

        /// <summary>コントローラー実行タスク</summary>
        private static Task _controllerTask;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // ★ログ初期化は最優先（以降の Logger.* を確実にファイル出力させる）
            Logger.InitializeFromAppConfig();

            // ★例外ハンドリング（落ちても原因追跡できるように）
            SetupGlobalExceptionHandlers();

            Logger.Info("[SftpTransferAgent] Main start.");

            try
            {
                Initialize();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                CreateNotifyIcon();

                _controller = new Controller();

                // ★バックグラウンド処理の例外を握りつぶさずログへ
                _controllerTask = Task.Run(() =>
                {
                    try
                    {
                        Logger.Info("[SftpTransferAgent] Controller.Run start.");
                        _controller.Run();
                        Logger.Info("[SftpTransferAgent] Controller.Run end.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Fatal("[SftpTransferAgent] Controller.Run crashed.", ex);

                        // ここでアプリ継続する/終了するは運用方針次第。
                        // 「常駐してるのに中身が死んだ」を避けたいなら Exit 推奨。
                        try { Application.Exit(); } catch { /* noop */ }
                    }
                });

                // 終了処理（終了時にログ、リソース破棄）
                Application.ApplicationExit += (s, e) =>
                {
                    try
                    {
                        Logger.Info("[SftpTransferAgent] ApplicationExit called.");

                        if (_controller != null)
                            _controller.Stop();

                        if (_notifyIcon != null)
                        {
                            _notifyIcon.Visible = false;
                            _notifyIcon.Dispose();
                            _notifyIcon = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("[SftpTransferAgent] Error on ApplicationExit.", ex);
                    }
                };

                // メッセージループ開始（フォームなしの常駐）
                Application.Run();
            }
            catch (Exception ex)
            {
                Logger.Fatal("[SftpTransferAgent] Unhandled exception in Main.", ex);
                // ここで再throwするかは運用方針次第。タスクスケジューラなら終了でOKが多い。
            }
            finally
            {
                Logger.Info("[SftpTransferAgent] Main end.");
            }
        }

        /// <summary>
        /// グローバル例外ハンドラ設定（UI/非UI/Task）
        /// </summary>
        private static void SetupGlobalExceptionHandlers()
        {
            // WinForms UIスレッドの未処理例外
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Logger.Fatal("[SftpTransferAgent] UI ThreadException occurred.", e.Exception);
            };

            // 非UIスレッドの未処理例外
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Fatal("[SftpTransferAgent] AppDomain UnhandledException occurred.", e.ExceptionObject as Exception);
            };

            // 観測されなかったTask例外
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Logger.Error("[SftpTransferAgent] UnobservedTaskException occurred.", e.Exception);
                e.SetObserved();
            };
        }

        /// <summary>
        /// タスクトレイ表示設定
        /// </summary>
        private static void CreateNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();

            _notifyIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _notifyIcon.ContextMenuStrip = ContextMenu();
            _notifyIcon.Text = "SftpTransferAgent";
            _notifyIcon.Visible = true;
        }

        /// <summary>
        /// コンテキストメニュー設定
        /// </summary>
        private static ContextMenuStrip ContextMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add("終了", null, (s, e) =>
            {
                try
                {
                    Logger.Info("[SftpTransferAgent] Exit menu clicked.");

                    if (_controller != null)
                        _controller.Stop();

                    Application.Exit();
                }
                catch (Exception ex)
                {
                    Logger.Error("[SftpTransferAgent] Error on exit menu.", ex);
                    try { Application.Exit(); } catch { /* noop */ }
                }
            });

            return menu;
        }

        /// <summary>
        /// 初期化
        /// </summary>
        private static void Initialize()
        {
            try
            {
                // コンフィグの初期化
                // SettingManager.Init(new List<string> { "./Settings/Setting.xml" });
                // SettingManager.InitWithAppSettings();

                // ApplicationInsights設定
                // AiInit.ApplicationInsightsSettings();

                Logger.Info("===== Initialize Completed =====");
            }
            catch (Exception ex)
            {
                // WinExeなので Console.WriteLine は実運用では見えない。ログに寄せる。
                Logger.Fatal("[SftpTransferAgent] Initialize Error.", ex);
                throw;
            }
        }
    }
}