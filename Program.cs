using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SftpTransferAgent.Common.Logging;

namespace SftpTransferAgent
{
    internal static class Program
    {
        private static Controller _controller;
        private static NotifyIcon _notifyIcon;
        private static Task _controllerTask;

        private static SynchronizationContext _uiContext;
        private static Icon _iconNormal;

        private static int _shutdownFlag = 0; // 0:通常 / 1:シャットダウン中
        private static int _exitCode = 0;

        private enum TrayStatus
        {
            Starting,
            Running,
            Error,
            Stopping
        }

        [STAThread]
        static void Main()
        {
            Logger.InitializeFromAppConfig();
            SetupGlobalExceptionHandlers();

            Logger.Info("Main start.");

            try
            {
                Initialize();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                _uiContext = SynchronizationContext.Current;

                CreateNotifyIcon();
                SetTrayStatus(TrayStatus.Starting, "起動中");

                // 起動通知（環境差で落ちないよう少し遅らせる）
                ScheduleStartupNotice();

                _controller = new Controller();

                _controllerTask = Task.Run(() =>
                {
                    try
                    {
                        Logger.Info("Controller.Run start.");
                        SetTrayStatus(TrayStatus.Running, "稼働中");

                        _controller.Run();

                        Logger.Info("Controller.Run end.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Fatal("Controller.Run crashed.", ex);
                        SetTrayStatus(TrayStatus.Error, "エラー（ログ確認）", showBalloon: true);

                        // 中身が死んだまま常駐しないよう終了へ
                        BeginShutdown("Controller crashed", exitCode: 1);
                    }
                });

                // ApplicationExit は「UI破棄だけ」に寄せる（ここで Stop するとブロックして詰まりやすい）
                Application.ApplicationExit += (s, e) =>
                {
                    try
                    {
                        Logger.Info("ApplicationExit called.");

                        if (_notifyIcon != null)
                        {
                            _notifyIcon.Visible = false;
                            _notifyIcon.Dispose();
                            _notifyIcon = null;
                        }

                        if (_iconNormal != null)
                        {
                            _iconNormal.Dispose();
                            _iconNormal = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error on ApplicationExit.", ex);
                    }
                };

                Application.Run();
            }
            catch (Exception ex)
            {
                Logger.Fatal("Unhandled exception in Main.", ex);
                BeginShutdown("Unhandled exception in Main", exitCode: 1);
            }
            finally
            {
                Logger.Info("Main end.");
            }
        }

        private static void SetupGlobalExceptionHandlers()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Logger.Fatal("UI ThreadException occurred.", e.Exception);
                SetTrayStatus(TrayStatus.Error, "エラー（ログ確認）", showBalloon: true);
                BeginShutdown("UI ThreadException", exitCode: 1);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Fatal("AppDomain UnhandledException occurred.", e.ExceptionObject as Exception);
                SetTrayStatus(TrayStatus.Error, "エラー（ログ確認）", showBalloon: true);
                BeginShutdown("AppDomain UnhandledException", exitCode: 1);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Logger.Error("UnobservedTaskException occurred.", e.Exception);
                e.SetObserved();
                SetTrayStatus(TrayStatus.Error, "エラー（ログ確認）", showBalloon: true);
                BeginShutdown("UnobservedTaskException", exitCode: 1);
            };
        }

        private static void CreateNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();

            _iconNormal = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _notifyIcon.Icon = _iconNormal;

            _notifyIcon.ContextMenuStrip = ContextMenu();
            _notifyIcon.Text = "SftpTransferAgent";
            _notifyIcon.Visible = true;

            // ダブルクリックでログフォルダを開く
            _notifyIcon.DoubleClick += (s, e) => OpenLogFolder();
        }

        private static ContextMenuStrip ContextMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add("ログフォルダを開く", null, (s, e) =>
            {
                Logger.Info("Open log folder clicked.");
                OpenLogFolder();
            });

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("終了", null, (s, e) =>
            {
                Logger.Info("Exit menu clicked.");
                BeginShutdown("User requested exit", exitCode: 0);
            });

            return menu;
        }

        /// <summary>
        /// 停止要求→待機→最後は強制終了でを確実に潰す
        /// </summary>
        private static void BeginShutdown(string reason, int exitCode)
        {
            // 多重に呼ばれても1回だけ実行
            if (Interlocked.Exchange(ref _shutdownFlag, 1) != 0) return;

            _exitCode = exitCode;

            Logger.Warning($"Shutdown requested. reason={reason}");
            SetTrayStatus(TrayStatus.Stopping, "停止中", showBalloon: false);

            // メニュー無効化（連打防止）
            PostToUi(() =>
            {
                try
                {
                    if (_notifyIcon?.ContextMenuStrip != null)
                        _notifyIcon.ContextMenuStrip.Enabled = false;
                }
                catch { /* noop */ }
            });

            // 停止処理はUIスレッドを塞がない
            Task.Run(() =>
            {
                try
                {
                    // 停止要求
                    try
                    {
                        _controller?.Stop();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error on Controller.Stop.", ex);
                        _exitCode = 1;
                    }

                    // Run が止まるのを少し待つ（止まらないときは次へ）
                    try
                    {
                        if (_controllerTask != null)
                        {
                            if (!_controllerTask.Wait(TimeSpan.FromSeconds(10)))
                                Logger.Warning("Controller task did not stop within 10 seconds.");
                        }
                    }
                    catch (AggregateException ae)
                    {
                        Logger.Error("Controller task ended with exception.", ae.Flatten());
                        _exitCode = 1;
                    }

                    // UIループ終了（これで通常はプロセスが終わる）
                    PostToUi(() =>
                    {
                        try { Application.ExitThread(); } catch { /* noop */ }
                    });

                    // アイコンだけ消えてプロセスが残る現象を確実に防ぐ
                    Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
                    {
                        Logger.Warning("Forcing process exit.");
                        Environment.Exit(_exitCode);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Fatal("Shutdown routine crashed.", ex);
                    Environment.Exit(1);
                }
            });
        }

        private static void SetTrayStatus(TrayStatus status, string shortMessage, bool showBalloon = false)
        {
            PostToUi(() =>
            {
                if (_notifyIcon == null) return;

                _notifyIcon.Text = $"SftpTransferAgent - {shortMessage}";

                switch (status)
                {
                    case TrayStatus.Starting:
                        _notifyIcon.Icon = SystemIcons.Information;
                        break;
                    case TrayStatus.Running:
                        _notifyIcon.Icon = _iconNormal ?? SystemIcons.Application;
                        break;
                    case TrayStatus.Error:
                        _notifyIcon.Icon = SystemIcons.Error;
                        break;
                    case TrayStatus.Stopping:
                        _notifyIcon.Icon = SystemIcons.Warning;
                        break;
                }

                if (showBalloon && SystemInformation.UserInteractive)
                {
                    _notifyIcon.BalloonTipTitle = "SftpTransferAgent";
                    _notifyIcon.BalloonTipText = shortMessage;
                    _notifyIcon.BalloonTipIcon = (status == TrayStatus.Error) ? ToolTipIcon.Error : ToolTipIcon.Info;
                    _notifyIcon.ShowBalloonTip(status == TrayStatus.Error ? 5000 : 3000);
                }
            });
        }

        private static void ScheduleStartupNotice()
        {
            if (!SystemInformation.UserInteractive) return;
            if (_notifyIcon == null) return;

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 800;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                if (_notifyIcon == null) return;
                _notifyIcon.BalloonTipTitle = "SftpTransferAgent";
                _notifyIcon.BalloonTipText = "起動しました。タスクトレイに常駐します。";
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                _notifyIcon.ShowBalloonTip(3000);
            };
            timer.Start();
        }

        private static void PostToUi(Action action)
        {
            try
            {
                if (action == null) return;
                var ctx = _uiContext;
                if (ctx != null) ctx.Post(_ => action(), null);
                else action();
            }
            catch
            {
                // UI更新で落とさない
            }
        }

        private static void OpenLogFolder()
        {
            try
            {
                var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
                Directory.CreateDirectory(logDir);
                Process.Start(logDir);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open log folder.", ex);
            }
        }

        private static void Initialize()
        {
            try
            {
                Logger.Info("===== Initialize Completed =====");
            }
            catch (Exception ex)
            {
                Logger.Fatal("Initialize Error.", ex);
                throw;
            }
        }
    }
}