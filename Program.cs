using System;
using System.IO;
using System.Linq;
using ComicArchiver.Services;

namespace ComicArchiver
{
    /// <summary>
    /// 应用程序入口点类。
    /// 支持双模式运行：根据命令行参数自动判断启动控制台 CLI 模式或 WPF 图形界面 (GUI) 模式。
    /// </summary>
    public class Program
    {
        /// <summary>
        /// 程序主入口点。
        /// [STAThread] 特性要求 Single-Threaded Apartment 模型，对于 WPF/WinForms GUI 至关重要。
        /// </summary>
        /// <param name="args">命令行传入的参数数组</param>
        [STAThread]
        public static void Main(string[] args)
        {
            // 判断是否包含 CLI 选项或命令行命令
            if (ShouldRunInCliMode(args))
            {
                // 附加到调用方终端控制台或创建控制台
                ConsoleHelper.AttachOrAllocConsole();
                RunCliMode(args);
                ConsoleHelper.ReleaseConsole();
                return;
            }

            // 无命令行分支参数时启动 WPF 界面
            LaunchGui();
        }

        /// <summary>
        /// 初始化并启动 WPF GUI 主窗口。
        /// </summary>
        private static void LaunchGui()
        {
            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow();
            app.Run(window);
        }

        /// <summary>
        /// 检查传入参数，判断是否应当在控制台命令行模式下运行。
        /// </summary>
        /// <param name="args">命令行参数数组</param>
        /// <returns>若符合 CLI 运行条件返回 true，否则返回 false</returns>
        private static bool ShouldRunInCliMode(string[] args)
        {
            if (args == null || args.Length == 0) return false;

            // 检查首个参数是否为特定命令或帮助开关
            string first = args[0].Trim().ToLowerInvariant().TrimStart('-', '/');
            if (first == "cbz" || first == "zip" || first == "h" || first == "help" || first == "?")
                return true;

            // 检查是否存在 CLI 参数标志 (如 -t, --type, -d, --dir, --directory, -b, --batch)
            if (args.Any(a => a.Equals("-t", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--type", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--dir", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--directory", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("-b", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("--batch", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        /// <summary>
        /// 执行控制台命令行打包任务逻辑。
        /// 解析命令行选项，输出格式化终端日志，并调用 ArchiverService 处理。
        /// </summary>
        /// <param name="args">命令行参数数组</param>
        private static void RunCliMode(string[] args)
        {
            // 打印帮助信息开关判定
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
            // 配置控制台进度与彩色日志回调
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

        /// <summary>
        /// 解析命令行参数并填充为 ArchiverOptions 对象。
        /// 支持简写（cbz/zip目录）、标准选项（-t, -d, -x, --direct, --keep-original等）与路径定位。
        /// </summary>
        /// <param name="args">命令行传入的参数数组</param>
        /// <returns>填充完毕的 ArchiverOptions 实例</returns>
        public static ArchiverOptions ParseCliOptions(string[] args)
        {
            var options = new ArchiverOptions();
            if (args == null || args.Length == 0) return options;

            string targetDir = Environment.CurrentDirectory;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = CleanArg(args[i]);
                string lower = arg.ToLowerInvariant();

                // 兼容经典命令语法: ComicArchiver cbz "D:\Comics"
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
                else if (lower == "-b" || lower == "--batch")
                {
                    options.Mode = BatchMode.Batch;
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

        /// <summary>
        /// 清理参数字符串中的两端空白及包裹的双引号/单引号。
        /// </summary>
        /// <param name="arg">原始参数文本</param>
        /// <returns>清理后的纯文本</returns>
        private static string CleanArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return string.Empty;
            return arg.Trim().Trim('"', '\'');
        }

        /// <summary>
        /// 向控制台打印 CLI 模式命令说明与用法帮助文档。
        /// </summary>
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
  -b, --batch            批量模式: 压缩输入目录下的所有目录的子目录
  -x, --exclude <pattern> 排除文件名通配符 (默认: *.db)
  --keep-original        压缩完成后保留原始文件夹
  -h, --help             显示帮助信息
");
        }
    }
}

