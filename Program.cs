using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ComicArchiver
{
    /// <summary>
    /// 应用程序入口点类。
    /// 管理单实例 Mutex、跨进程命名管道 (IPC) 参数传递以及前台窗口激活。
    /// </summary>
    public class Program
    {
        private static Mutex _mutex;
        private static CancellationTokenSource _ipcServerCts;

        private static string GetPipeName()
        {
            return $"ComicArchiver_IPC_Pipe_{Environment.UserName}";
        }

        /// <summary>
        /// 程序主入口点。
        /// [STAThread] 特性要求 Single-Threaded Apartment 模型，对于 WPF/WinForms GUI 至关重要。
        /// </summary>
        /// <param name="args">命令行传入的参数数组</param>
        [STAThread]
        public static void Main(string[] args)
        {
            // 解决 WPF 在自定义 Exe 文件名时查找 pack URI 资源抛出 FileNotFoundException 的问题
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                try
                {
                    var targetName = new System.Reflection.AssemblyName(resolveArgs.Name).Name;
                    var entryAsm = typeof(Program).Assembly;
                    if (targetName == "ComicArchiver" || targetName == entryAsm.GetName().Name)
                    {
                        return entryAsm;
                    }
                }
                catch { }
                return null;
            };

            const string appName = "ComicArchiver_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                // 向已有运行中的主实例发送命令行参数
                if (args != null && args.Length > 0)
                {
                    SendArgsToExistingInstance(args);
                }
                ActivateExistingWindow();
                return;
            }

            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow(args);

            // 启动 IPC 命名管道服务端，接收后续新实例的命令行参数
            StartIpcServer(window);

            app.Run(window);

            _ipcServerCts?.Cancel();
            GC.KeepAlive(_mutex);
        }

        /// <summary>
        /// 启动命名管道服务端后台监听任务。
        /// </summary>
        private static void StartIpcServer(MainWindow mainWindow)
        {
            _ipcServerCts = new CancellationTokenSource();
            var token = _ipcServerCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(
                            GetPipeName(),
                            PipeDirection.In,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous))
                        {
                            await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                            using (var reader = new StreamReader(server, Encoding.UTF8))
                            {
                                string countLine = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (int.TryParse(countLine, out int count) && count > 0)
                                {
                                    var argsList = new List<string>(count);
                                    for (int i = 0; i < count; i++)
                                    {
                                        string arg = await reader.ReadLineAsync().ConfigureAwait(false);
                                        if (arg != null)
                                        {
                                            argsList.Add(arg);
                                        }
                                    }

                                    // 分发到主窗口的 UI 线程执行
                                    _ = mainWindow.Dispatcher.InvokeAsync(() =>
                                    {
                                        mainWindow.HandleExternalArgs(argsList.ToArray());
                                        ActivateCurrentWindow();
                                    });
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"IPC 服务端异常: {ex.Message}");
                        try
                        {
                            await Task.Delay(500, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }, token);
        }

        /// <summary>
        /// 将新实例收到的命令行参数写入命名管道发送给主实例。
        /// </summary>
        private static void SendArgsToExistingInstance(string[] args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", GetPipeName(), PipeDirection.Out))
                {
                    client.Connect(1000); // 1 秒超时
                    using (var writer = new StreamWriter(client, Encoding.UTF8))
                    {
                        writer.WriteLine(args.Length);
                        foreach (var arg in args)
                        {
                            writer.WriteLine(arg);
                        }
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"向已有实例传递参数失败: {ex.Message}");
            }
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

        private static void ActivateCurrentWindow()
        {
            IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
    }
}
