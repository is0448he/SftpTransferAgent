using SftpTransferAgent.Common;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SftpTransferAgent
{
    internal static class Program
    {
        /// <summary>コントローラー</summary>
        private static Controller _controller;

        private static NotifyIcon _notifyIcon;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Initialize();

            Logger.Info("SftpTransferAgent 'Main' method called");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CreateNotifyIcon();

            _controller = new Controller();
            Task.Run(() => _controller.Run());

            Application.Run();

            Logger.Info("SftpTransferAgent 'Main' end");
        }

        /// <summary>
        /// タスクトレイ表示設定
        /// </summary>
        private static void CreateNotifyIcon()
        {
            var icon = new NotifyIcon();

            icon.Icon = new Icon("Icon.ico");
            icon.ContextMenuStrip = ContextMenu();
            icon.Text = "SftpTransferAgent";
            icon.Visible = true;
        }

        // <summary>
        /// コンテキストメニュー設定
        /// </summary>
        /// <returns></returns>
        private static ContextMenuStrip ContextMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add("終了", null, (s, e) =>
            {
                if (_controller != null)
                {
                    _controller.Stop();
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
                var _settings = CommonSettingValues.Load();

                // ApplicationInsights設定
                // AiInit.ApplicationInsightsSettings();

                Logger.Info("===== Initialize Completed =====");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Initialize Error: {ex.Message}");
                Logger.Error(ex.Message, ex);
                throw;
            }
        }
    }
}