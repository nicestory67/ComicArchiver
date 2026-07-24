using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ComicArchiver.Services;

namespace ComicArchiver
{
    /// <summary>
    /// 命令行模式下的操作类型枚举。
    /// </summary>
    public enum CliOperation
    {
        /// <summary> 打包压缩模式 </summary>
        Compress,
        /// <summary> 解压还原模式 </summary>
        Extract
    }

    /// <summary>
    /// 命令行参数解析结果包装类。
    /// </summary>
    public class CliParseResult
    {
        /// <summary> 操作类型（打包或解压） </summary>
        public CliOperation Operation { get; set; } = CliOperation.Compress;

        /// <summary> 归档服务配置选项 </summary>
        public ArchiverOptions Options { get; set; } = new ArchiverOptions();

        /// <summary> 是否请求显示帮助信息 </summary>
        public bool ShowHelp { get; set; } = false;

        /// <summary> 参数解析错误消息 </summary>
        public string ErrorMessage { get; set; } = null;
    }

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
            // 判断是否在 CLI 命令行模式下运行（带命令行参数时运行 CLI，无参数时启动 GUI）
            if (ShouldRunInCliMode(args))
            {
                // 附加到调用方终端控制台或创建控制台
                ConsoleHelper.AttachOrAllocConsole();
                RunCliMode(args);
                ConsoleHelper.ReleaseConsole();
                return;
            }

            // 无命令行参数时直接启动 WPF GUI 界面
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
        /// 只要传入了命令行参数，即进入 CLI 模式；无参数时进入 GUI 模式。
        /// </summary>
        /// <param name="args">命令行参数数组</param>
        /// <returns>若符合 CLI 运行条件返回 true，否则返回 false</returns>
        private static bool ShouldRunInCliMode(string[] args)
        {
            return args != null && args.Length > 0;
        }

        /// <summary>
        /// 执行控制台命令行打包/解压任务逻辑。
        /// 解析命令行选项，输出格式化终端日志，并调用 ArchiverService 处理。
        /// </summary>
        /// <param name="args">命令行参数数组</param>
        private static void RunCliMode(string[] args)
        {
            var parseResult = ParseCliOptions(args);

            if (parseResult.ShowHelp)
            {
                PrintHelp();
                return;
            }

            if (!string.IsNullOrEmpty(parseResult.ErrorMessage))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"错误: {parseResult.ErrorMessage}");
                Console.ResetColor();
                Console.WriteLine("提示: 使用 -h 或 --help 查看用法帮助。");
                return;
            }
            
            Console.WriteLine();
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

            using (var cts = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancelHandler = (sender, eventArgs) =>
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[系统] 收到中断信号 (Ctrl+C)，正在取消当前任务...");
                    Console.ResetColor();
                    eventArgs.Cancel = true;
                    cts.Cancel();
                };

                Console.CancelKeyPress += cancelHandler;

                try
                {
                    if (parseResult.Operation == CliOperation.Extract)
                    {
                        archiver.Extract(parseResult.Options, progress, cts.Token);
                    }
                    else
                    {
                        archiver.Process(parseResult.Options, progress, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("提示: 操作已被用户取消。");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"执行失败: {ex.Message}");
                    Console.ResetColor();
                }
                finally
                {
                    try { Console.CancelKeyPress -= cancelHandler; } catch { }
                }
            }
        }

        /// <summary>
        /// 解析命令行参数并填充为 CliParseResult 对象。
        /// 统一采用 "-参数" 选项模式，格式为: ComicArchiver [选项] [目标目录/文件...]
        /// </summary>
        /// <param name="args">命令行传入的参数数组</param>
        /// <returns>填充完毕的 CliParseResult 实例</returns>
        public static CliParseResult ParseCliOptions(string[] args)
        {
            var result = new CliParseResult();
            if (args == null || args.Length == 0) return result;

            var options = result.Options;
            bool explicitOperation = false;

            for (int i = 0; i < args.Length; i++)
            {
                string rawArg = args[i];
                string arg = CleanArg(rawArg);
                string lower = arg.ToLowerInvariant();

                if (lower == "-h" || lower == "--help" || lower == "/?" || lower == "help")
                {
                    result.ShowHelp = true;
                    return result;
                }
                else if (lower == "-e" || lower == "--extract" || lower == "-u" || lower == "--unzip")
                {
                    result.Operation = CliOperation.Extract;
                    explicitOperation = true;
                }
                else if (lower == "-c" || lower == "--compress" || lower == "--pack")
                {
                    result.Operation = CliOperation.Compress;
                    explicitOperation = true;
                }
                else if (lower == "-z" || lower == "--zip")
                {
                    options.ArchiveType = "zip";
                }
                else if (lower == "--cbz")
                {
                    options.ArchiveType = "cbz";
                }
                else if (lower == "-t" || lower == "--type" || lower == "-f" || lower == "--format")
                {
                    if (i + 1 < args.Length)
                    {
                        options.ArchiveType = CleanArg(args[++i]).ToLowerInvariant();
                    }
                    else
                    {
                        result.ErrorMessage = "选项 -t/--type 需要指定格式 (cbz 或 zip)";
                        return result;
                    }
                }
                else if (rawArg == "-D" || lower == "-del" || lower == "--delete" || lower == "--delete-original")
                {
                    options.DeleteOriginalFolder = true;
                }
                else if (lower == "-d" || lower == "--dir" || lower == "--directory")
                {
                    if (i + 1 < args.Length)
                    {
                        options.TargetPaths.Add(CleanArg(args[++i]));
                    }
                    else
                    {
                        result.ErrorMessage = "选项 -d/--dir 需要指定目标路径";
                        return result;
                    }
                }
                else if (lower == "-b" || lower == "--batch")
                {
                    options.Mode = BatchMode.Batch;
                }
                else if (lower == "-s" || lower == "--subfolders")
                {
                    options.Mode = BatchMode.SubFolders;
                }
                else if (lower == "-m" || lower == "--mode")
                {
                    if (i + 1 < args.Length)
                    {
                        string modeVal = CleanArg(args[++i]).ToLowerInvariant();
                        if (modeVal == "batch" || modeVal == "b")
                        {
                            options.Mode = BatchMode.Batch;
                        }
                        else if (modeVal == "subfolders" || modeVal == "subfolder" || modeVal == "s")
                        {
                            options.Mode = BatchMode.SubFolders;
                        }
                        else
                        {
                            result.ErrorMessage = $"未知的模式 '{modeVal}'，请使用 s/subfolders 或 b/batch";
                            return result;
                        }
                    }
                    else
                    {
                        result.ErrorMessage = "选项 -m/--mode 需要指定模式 (s/subfolders 或 b/batch)";
                        return result;
                    }
                }
                else if (lower == "-k" || lower == "--keep" || lower == "--keep-original")
                {
                    options.DeleteOriginalFolder = false;
                }
                else if (lower == "-x" || lower == "--exclude")
                {
                    if (i + 1 < args.Length)
                    {
                        options.ExcludePattern = CleanArg(args[++i]);
                    }
                    else
                    {
                        result.ErrorMessage = "选项 -x/--exclude 需要指定排除规则";
                        return result;
                    }
                }
                else if (!arg.StartsWith("-") && !arg.StartsWith("/"))
                {
                    options.TargetPaths.Add(arg);
                }
                else
                {
                    result.ErrorMessage = $"无法识别的命令行选项 '{arg}'";
                    return result;
                }
            }

            if (options.TargetPaths.Count == 0)
            {
                options.TargetPaths.Add(Environment.CurrentDirectory);
            }
            else
            {
                options.TargetPaths = options.TargetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            // 若用户未显式指定 -c 或 -e，根据目标路径类型智能自动推断
            if (!explicitOperation)
            {
                bool hasArchiveFiles = options.TargetPaths.Any(p =>
                    File.Exists(p) && (p.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));

                if (hasArchiveFiles)
                {
                    result.Operation = CliOperation.Extract;
                }
            }

            return result;
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
        /// 向控制台打印统一规范的 CLI 模式帮助文档。
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine(@"
        ComicArchiver .NET 4.8 漫画打包解压工具 (CLI)

        用法:
        ComicArchiver [选项] [目标目录/文件...]

        选项:
        -c / -e              模式: -c (打包模式, 默认) / -e (解压模式)
        -z / -t <cbz|zip>    格式: -z (ZIP格式) 或 -t cbz/zip (指定格式, 默认: cbz)
        -s / -b              层级: -s (单层子目录, 默认) / -b (双层嵌套批量)
        -k / -D              清理: -k (保留原文件夹/文件) / -D (删除原文件夹/文件, 默认)
        -d <path>            目标路径 (可指定多个)
        -x <pattern>         排除通配符 (默认: *.db)
        -h                   显示帮助信息

        示例:
        1. 打包 D:\Comics 目录下的子文件夹为 CBZ (删除原文件夹):
            ComicArchiver ""D:\Comics""

        2. 打包为 ZIP 格式并保留原文件夹:
            ComicArchiver -z -k ""D:\Comics""

        3. 批量解压 D:\Comics 目录下的 CBZ/ZIP 压缩包:
            ComicArchiver -e ""D:\Comics""

        4. 嵌套批量解压并保留原压缩包:
            ComicArchiver -e -b -k ""D:\Comics""

");
        }
    }
}


