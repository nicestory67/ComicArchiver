using System;
using System.IO;
using System.Linq;
using ComicArchiver.Services;

namespace ComicArchiver
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (ShouldRunInCliMode(args))
            {
                ConsoleHelper.AttachOrAllocConsole();
                RunCliMode(args);
                ConsoleHelper.ReleaseConsole();
                return;
            }

            LaunchGui();
        }

        private static void LaunchGui()
        {
            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow();
            app.Run(window);
        }

        private static bool ShouldRunInCliMode(string[] args)
        {
            if (args == null || args.Length == 0) return false;

            string first = args[0].Trim().ToLowerInvariant().TrimStart('-', '/');
            if (first == "cbz" || first == "zip" || first == "h" || first == "help" || first == "?")
                return true;

            if (args.Any(a => a.Equals("-t", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--type", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--dir", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--directory", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private static void RunCliMode(string[] args)
        {
            if (args.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("/?")))
            {
                PrintHelp();
                return;
            }

            var options = ParseCliOptions(args);
            if (options == null)
            {
                Console.WriteLine("错误: 参数无法识别。使用 -h 查看帮助。");
                return;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine(" ComicArchiver .NET 4.8 (LZMA SDK) - 命令行模式");
            Console.WriteLine("==================================================");

            var archiver = new ArchiverService();
            var progress = new Progress<ArchiveProgressReport>(report =>
            {
                switch (report.Level)
                {
                    case LogLevel.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case LogLevel.Success:
                        Console.ForegroundColor = ConsoleColor.Green;
                        break;
                    default:
                        Console.ResetColor();
                        break;
                }
                Console.WriteLine(report.Message);
                Console.ResetColor();
            });

            try
            {
                archiver.Process(options, progress);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"执行失败: {ex.Message}");
                Console.ResetColor();
            }
        }

        public static ArchiverOptions ParseCliOptions(string[] args)
        {
            var options = new ArchiverOptions();
            if (args == null || args.Length == 0) return options;

            string targetDir = Environment.CurrentDirectory;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = CleanArg(args[i]);
                string lower = arg.ToLowerInvariant();

                if (lower == "cbz" || lower == "zip")
                {
                    options.ArchiveType = lower;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        targetDir = CleanArg(args[++i]);
                    }
                }
                else if (lower == "-t" || lower == "--type")
                {
                    if (i + 1 < args.Length) { options.ArchiveType = CleanArg(args[++i]).ToLowerInvariant(); }
                }
                else if (lower == "-d" || lower == "--dir" || lower == "--directory")
                {
                    if (i + 1 < args.Length) { targetDir = CleanArg(args[++i]); }
                }
                else if (lower == "--direct" || lower == "--batch-direct")
                {
                    options.Mode = BatchMode.DirectFolders;
                }
                else if (lower == "--keep-original")
                {
                    options.DeleteOriginalFolder = false;
                }
                else if (lower == "-x" || lower == "--exclude")
                {
                    if (i + 1 < args.Length) { options.ExcludePattern = CleanArg(args[++i]); }
                }
                else if (!arg.StartsWith("-") && Directory.Exists(arg))
                {
                    targetDir = arg;
                }
            }

            options.TargetPaths.Add(targetDir);
            return options;
        }

        private static string CleanArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return string.Empty;
            return arg.Trim().Trim('"', '\'');
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"ComicArchiver .NET 4.8 漫画文件夹打包工具

用法:
  ComicArchiver.exe <cbz|zip> [目标目录]
  ComicArchiver.exe [选项]

位置参数:
  ComicArchiver.exe cbz               对当前目录下的所有子文件夹进行 CBZ 打包
  ComicArchiver.exe zip               对当前目录下的所有子文件夹进行 ZIP 打包
  ComicArchiver.exe cbz ""D:\Comics""  对指定目录下的所有子文件夹进行 CBZ 打包

选项:
  -t, --type <cbz|zip>   指定压缩后缀格式 (默认: cbz)
  -d, --dir <path>       指定目标工作目录 (默认: 当前目录)
  --direct               批量直压模式: 将选定目录本身直接压缩 (而非子文件夹)
  -x, --exclude <pattern> 排除文件名通配符 (默认: *.db)
  --keep-original        压缩完成后保留原始文件夹
  -h, --help             显示帮助信息
");
        }
    }
}
