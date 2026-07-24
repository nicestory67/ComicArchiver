using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ComicArchiver.Services
{
    /// <summary>
    /// 控制台辅助工具类。
    /// 提供针对 Win32 API 的封装，用于在 GUI 应用程序运行于命令行（CMD / PowerShell）模式时
    /// 动态附加控制台窗口或分配独立控制台，并对标准输入输出流进行正确重定向。
    /// </summary>
    public static class ConsoleHelper
    {
        /// <summary>
        /// 将当前进程附加到指定进程的控制台。
        /// </summary>
        /// <param name="dwProcessId">目标进程 ID，传入 -1 (ATTACH_PARENT_PROCESS) 表示附加到父进程控制台</param>
        /// <returns>成功返回 true，失败返回 false</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        /// <summary>
        /// 为当前进程分配一个新的控制台窗口。
        /// </summary>
        /// <returns>成功返回 true，失败返回 false</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        /// <summary>
        /// 分离（释放）当前进程附加或创建的控制台。
        /// </summary>
        /// <returns>成功返回 true，失败返回 false</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        /// <summary>
        /// 创建或打开文件或 I/O 设备（此处用于打开控制台输出设备 "CONOUT$"）。
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            int shareMode,
            IntPtr securityAttributes,
            int creationDisposition,
            int flagsAndAttributes,
            IntPtr templateFile);

        /// <summary>
        /// 附加到父进程控制台的标识常数。
        /// </summary>
        private const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>
        /// Win32 通用写权限掩码。
        /// </summary>
        private const uint GENERIC_WRITE = 0x40000000;

        /// <summary>
        /// Win32 共享写模式标识。
        /// </summary>
        private const int FILE_SHARE_WRITE = 0x00000002;

        /// <summary>
        /// Win32 打开现有设备/文件标识。
        /// </summary>
        private const int OPEN_EXISTING = 3;

        /// <summary>
        /// 获取当前是否已附加到控制台。
        /// </summary>
        public static bool IsConsoleAttached { get; private set; }

        /// <summary>
        /// 尝试附加到父进程控制台（例如终端命令行窗口）。
        /// 如果附加成功，将重新绑定控制台的标准输出/错误流，确保 Console.WriteLine 能正常输出；
        /// 如果附加失败，尝试直接绑定系统标准输出流。
        /// </summary>
        public static void AttachOrAllocConsole()
        {
            if (IsConsoleAttached) return;

            // 优先尝试附加到启动本程序的终端控制台（父进程）
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                IsConsoleAttached = true;
                InitializeConsoleStreams();
            }
            else
            {
                // 回退方案：直接绑定现有的系统 stdout/stderr 标准流
                try
                {
                    var stdout = Console.OpenStandardOutput();
                    if (stdout != null && stdout != Stream.Null)
                    {
                        var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };
                        Console.SetOut(writer);
                        Console.SetError(writer);
                    }
                }
                catch
                {
                    // 忽略标准流绑定异常
                }
            }
        }

        /// <summary>
        /// 初始化控制台输出流。通过打开 "CONOUT$" 句柄重定向 System.Console 的 Out 和 Error 流，
        /// 并使用系统 OEM 编码页保证命令行中中文等非 ASCII 字符正常显示无乱码。
        /// </summary>
        private static void InitializeConsoleStreams()
        {
            try
            {
                // 获取 Win32 控制台输出设备句柄 CONOUT$
                IntPtr stdOutHandle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (stdOutHandle != IntPtr.Zero && stdOutHandle != new IntPtr(-1))
                {
                    var safeHandle = new SafeFileHandle(stdOutHandle, true);
                    var fileStream = new FileStream(safeHandle, FileAccess.Write);
                    // 使用当前系统区域设置对应的 OEM 编码（如 GBK / CodePage 936）
                    var encoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    var standardOutput = new StreamWriter(fileStream, encoding) { AutoFlush = true };
                    Console.SetOut(standardOutput);
                    Console.SetError(standardOutput);
                }
            }
            catch
            {
                // 若流重定向失败，忽略异常以避免阻断程序运行
            }
        }

        /// <summary>
        /// 释放附加的控制台窗口，断开控制台连接。
        /// </summary>
        public static void ReleaseConsole()
        {
            if (IsConsoleAttached)
            {
                FreeConsole();
                IsConsoleAttached = false;
            }
        }
    }
}

