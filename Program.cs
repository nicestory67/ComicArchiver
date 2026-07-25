using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
namespace ComicArchiver
{
    /// <summary>
    /// 应用程序入口点类。
    /// </summary>
    public class Program
    {
        private static Mutex _mutex;

        /// <summary>
        /// 程序主入口点。
        /// [STAThread] 特性要求 Single-Threaded Apartment 模型，对于 WPF/WinForms GUI 至关重要。
        /// </summary>
        /// <param name="args">命令行传入的参数数组</param>
        [STAThread]
        public static void Main(string[] args)
        {
            const string appName = "ComicArchiver_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                ActivateExistingWindow();
                return;
            }

            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow(args);
            app.Run(window);

            GC.KeepAlive(_mutex);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private static void ActivateExistingWindow()
        {
            Process current = Process.GetCurrentProcess();
            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id != current.Id)
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    break;
                }
            }
        }
    }
}
