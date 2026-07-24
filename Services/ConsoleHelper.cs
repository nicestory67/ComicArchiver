using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ComicArchiver.Services
{
    public static class ConsoleHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            int shareMode,
            IntPtr securityAttributes,
            int creationDisposition,
            int flagsAndAttributes,
            IntPtr templateFile);

        private const int ATTACH_PARENT_PROCESS = -1;
        private const uint GENERIC_WRITE = 0x40000000;
        private const int FILE_SHARE_WRITE = 0x00000002;
        private const int OPEN_EXISTING = 3;

        public static bool IsConsoleAttached { get; private set; }

        public static void AttachOrAllocConsole()
        {
            if (IsConsoleAttached) return;

            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                IsConsoleAttached = true;
                InitializeConsoleStreams();
            }
            else
            {
                // Fallback: Bind to standard stdout/stderr streams if available
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
                catch { }
            }
        }

        private static void InitializeConsoleStreams()
        {
            try
            {
                IntPtr stdOutHandle = CreateFile("CONOUT$", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (stdOutHandle != IntPtr.Zero && stdOutHandle != new IntPtr(-1))
                {
                    var safeHandle = new SafeFileHandle(stdOutHandle, true);
                    var fileStream = new FileStream(safeHandle, FileAccess.Write);
                    var encoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    var standardOutput = new StreamWriter(fileStream, encoding) { AutoFlush = true };
                    Console.SetOut(standardOutput);
                    Console.SetError(standardOutput);
                }
            }
            catch
            {
                // Ignore if stream re-binding fails
            }
        }

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
