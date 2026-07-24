using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ComicArchiver.Services
{
    /// <summary>
    /// 控制台辅助工具类。
    /// 提供针对 Win32 API 的封装，用于在 WinExe GUI 应用程序运行于命令行（CMD / PowerShell）模式时
    /// 动态附加父进程控制台，并正确重定向 Win32 与 .NET System.Console 标准输出流。
    /// </summary>
    public static class ConsoleHelper
    {
        /// <summary>
        /// 将当前进程附加到指定进程的控制台。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        /// <summary>
        /// 为当前进程分配一个新的控制台窗口。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        /// <summary>
        /// 分离（释放）当前进程附加或创建的控制台。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        /// <summary>
        /// 获取 Win32 标准句柄。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        /// <summary>
        /// 设置 Win32 标准句柄。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr hFile);

        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;
        private const uint GENERIC_WRITE = 0x40000000;
        private const int FILE_SHARE_WRITE = 0x00000002;
        private const int OPEN_EXISTING = 3;

        private const uint FILE_TYPE_DISK = 0x0001;
        private const uint FILE_TYPE_PIPE = 0x0003;

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

            // 1. 检查 stdout 是否已被 Shell 重定向到文件或管道 (如 > log.txt 或 | findstr)
            IntPtr existingOut = GetStdHandle(STD_OUTPUT_HANDLE);
            if (existingOut != IntPtr.Zero && existingOut != new IntPtr(-1))
            {
                uint fileType = GetFileType(existingOut);
                if (fileType == FILE_TYPE_DISK || fileType == FILE_TYPE_PIPE)
                {
                    try
                    {
                        var safeHandle = new SafeFileHandle(existingOut, false);
                        var fileStream = new FileStream(safeHandle, FileAccess.Write);
                        var writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };
                        Console.SetOut(writer);
                        Console.SetError(writer);
                        IsConsoleAttached = true;
                        return;
                    }
                    catch
                    {
                    }
                }
            }

            // 2. 交互式终端：附加到启动本程序的终端控制台（父进程，如 CMD / PowerShell）
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                IsConsoleAttached = true;
                InitializeConsoleStreams();
                return;
            }

            // 3. 兜底回退：获取系统标准输出流
            try
            {
                var stdout = Console.OpenStandardOutput();
                if (stdout != null && stdout != Stream.Null)
                {
                    var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    var writer = new StreamWriter(stdout, encoding) { AutoFlush = true };
                    Console.SetOut(writer);
                    Console.SetError(writer);
                    IsConsoleAttached = true;
                }
            }
            catch
            {
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
                IntPtr stdOutHandle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (stdOutHandle != IntPtr.Zero && stdOutHandle != new IntPtr(-1))
                {
                    // 重置 Win32 标准句柄，使 .NET 能够识别终端句柄
                    SetStdHandle(STD_OUTPUT_HANDLE, stdOutHandle);
                    SetStdHandle(STD_ERROR_HANDLE, stdOutHandle);

                    Encoding encoding;
                    try
                    {
                        encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    }
                    catch
                    {
                        encoding = Encoding.Default;
                    }

                    Console.OutputEncoding = encoding;
                    var safeHandle = new SafeFileHandle(stdOutHandle, false);
                    var fileStream = new FileStream(safeHandle, FileAccess.Write);
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

