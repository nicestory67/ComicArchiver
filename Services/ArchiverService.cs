using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ComicArchiver.Services
{
    /// <summary>
    /// 日志级别枚举。
    /// 用于区分进度报告与界面日志记录的消息类型。
    /// </summary>
    public enum LogLevel
    {
        /// <summary> 普通信息 </summary>
        Info,
        /// <summary> 成功完成 </summary>
        Success,
        /// <summary> 警告消息 </summary>
        Warning,
        /// <summary> 错误消息 </summary>
        Error
    }

    /// <summary>
    /// 归档打包进度报告类。
    /// 包含当前处理的文件夹名称、全局进度百分比、日志消息及级别。
    /// </summary>
    public class ArchiveProgressReport
    {
        /// <summary> 日志消息文本 </summary>
        public string Message { get; set; }

        /// <summary> 消息日志级别 </summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary> 已处理完成的文件夹数量 </summary>
        public int ProcessedFolders { get; set; }

        /// <summary> 需要处理的总文件夹数量 </summary>
        public int TotalFolders { get; set; }

        /// <summary> 当前总体完成百分比 (0 - 100) </summary>
        public double Percentage => TotalFolders > 0 ? (double)ProcessedFolders / TotalFolders * 100 : 0;

        /// <summary> 当前正在处理的文件夹名称 </summary>
        public string CurrentFolder { get; set; }
    }

    /// <summary>
    /// 归档处理配置参数类。
    /// </summary>
    public class ArchiverOptions
    {
        /// <summary> 目标路径列表（可包含一个或多个文件夹路径） </summary>
        public List<string> TargetPaths { get; set; } = new List<string>();

        /// <summary> 目标压缩扩展名格式（支持 cbz 或 zip，默认: cbz） </summary>
        public string ArchiveType { get; set; } = "cbz";

        /// <summary> 压缩成功后是否删除原始文件夹（默认: true） </summary>
        public bool DeleteOriginalFolder { get; set; } = true;

        /// <summary> 额外包含的文件通配符规则（多个格式可用 ';' 或 ',' 分隔，默认: *.xml） </summary>
        public string AdditionalIncludePattern { get; set; } = "*.xml";

        /// <summary> 最大并发处理线程数（默认: 10，范围: 1 - 32） </summary>
        public int MaxDegreeOfParallelism { get; set; } = 10;
    }

    /// <summary>
    /// 漫画文件夹打包与归档核心服务类。
    /// 提供文件夹检索、Zip/CBZ 结构生成以及源文件夹清理等功能。
    /// </summary>
    public class ArchiverService
    {

        /// <summary>
        /// 异步执行漫画归档打包流程。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public async Task<(int Total, int Success)> ProcessAsync(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => Process(options, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 异步执行漫画归档解压流程。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public async Task<(int Total, int Success)> ExtractAsync(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => Extract(options, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 同步执行漫画归档解压流程。
        /// 根据 Options 的配置扫描待解压的 CBZ/ZIP 归档、解压至同名文件夹并可选清理原压缩包。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public (int Total, int Success) Extract(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options.TargetPaths == null || options.TargetPaths.Count == 0)
            {
                Report(progress, "错误: 未选择任何目标目录。", LogLevel.Error);
                return (0, 0);
            }

            List<string> archivesToExtract = new List<string>();

            // 根据模式搜寻待解压的压缩包文件
            foreach (var path in options.TargetPaths)
            {
                if (Directory.Exists(path))
                {
                    var files = SafeGetFiles(path, "*.*")
                        .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    archivesToExtract.AddRange(files);
                }
                else if (File.Exists(path))
                {
                    if (path.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        archivesToExtract.Add(path);
                    }
                }
            }

            archivesToExtract = archivesToExtract.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            int total = archivesToExtract.Count;
            if (total == 0)
            {
                Report(progress, "提示: 未找到需要解压的 CBZ/ZIP 压缩包。", LogLevel.Warning);
                return (0, 0);
            }

            int maxDegree = Math.Max(1, Math.Min(32, options.MaxDegreeOfParallelism));
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegree,
                CancellationToken = cancellationToken
            };

            Report(progress, $"开始批量解压处理，共 {total} 个压缩包文件，并发线程数: {maxDegree}", LogLevel.Info);

            int processedCount = 0;
            int successCount = 0;

            try
            {
                Parallel.ForEach(archivesToExtract, parallelOptions, archivePath =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string archiveName = Path.GetFileName(archivePath);
                    string archiveNameWithoutExt = Path.GetFileNameWithoutExtension(archivePath);
                    string parentDir = Path.GetDirectoryName(archivePath) ?? Path.GetPathRoot(archivePath) ?? archivePath;
                    string targetFolder = Path.Combine(parentDir, archiveNameWithoutExt);

                    Report(progress, $"[解压中] 正在解压 : {archiveName}", LogLevel.Info, processedCount, total, archiveName);

                    bool extractSuccess = false;
                    try
                    {
                        ExtractArchiveToDirectory(archivePath, targetFolder, cancellationToken);
                        extractSuccess = true;
                    }
                    catch (OperationCanceledException)
                    {
                        try { if (Directory.Exists(targetFolder)) ForceDeleteDirectory(targetFolder); } catch (Exception e) { Report(progress, $"回滚清理中断的文件夹失败: {e.Message}", LogLevel.Warning); }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        int currentDone = Interlocked.Increment(ref processedCount);
                        Report(progress, $"[{currentDone:D2}/{total:D2}] 解压失败 : {ex.Message}", LogLevel.Error, currentDone, total, archiveName);
                        try { if (Directory.Exists(targetFolder)) ForceDeleteDirectory(targetFolder); } catch (Exception e) { Report(progress, $"解压失败后清理残留文件夹失败: {e.Message}", LogLevel.Warning); }
                        return;
                    }

                    // 解压成功且开启了“清理原文件”选项时删除原压缩包
                    if (extractSuccess && options.DeleteOriginalFolder)
                    {
                        try
                        {
                            File.Delete(archivePath);
                            int currentDone = Interlocked.Increment(ref processedCount);
                            Interlocked.Increment(ref successCount);
                            Report(progress, $"[{currentDone:D2}/{total:D2}] 解压并清理成功 : {archiveNameWithoutExt}", LogLevel.Success, currentDone, total, archiveName);
                        }
                        catch (Exception ex)
                        {
                            int currentDone = Interlocked.Increment(ref processedCount);
                            Interlocked.Increment(ref successCount);
                            Report(progress, $"[{currentDone:D2}/{total:D2}] 解压成功但清理失败 : {ex.Message}", LogLevel.Warning, currentDone, total, archiveName);
                        }
                    }
                    else if (extractSuccess)
                    {
                        int currentDone = Interlocked.Increment(ref processedCount);
                        Interlocked.Increment(ref successCount);
                        Report(progress, $"[{currentDone:D2}/{total:D2}] 解压成功 : {archiveNameWithoutExt}", LogLevel.Success, currentDone, total, archiveName);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            Report(progress, $"==== 批量解压完成！成功率: {successCount}/{total} ====", LogLevel.Success, processedCount, total);
            return (total, successCount);
        }

        /// <summary>
        /// 将指定的 ZIP / CBZ 压缩包解压至目标文件夹。
        /// 兼容多层级目录结构与文件覆盖。
        /// </summary>
        /// <param name="archivePath">压缩包路径</param>
        /// <param name="destinationDir">目标输出目录</param>
        /// <param name="cancellationToken">取消令牌</param>
        private void ExtractArchiveToDirectory(
            string archivePath,
            string destinationDir,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destinationDir);
            string fullDestDir = Path.GetFullPath(destinationDir);
            if (!fullDestDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDestDir += Path.DirectorySeparatorChar;
            }

            using (var zipStream = File.OpenRead(archivePath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, false, System.Text.Encoding.Default))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 处理空文件夹条目
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        string dirPath = Path.GetFullPath(Path.Combine(destinationDir, entry.FullName));
                        if (dirPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.CreateDirectory(dirPath);
                        }
                        continue;
                    }

                    string destFilePath = Path.GetFullPath(Path.Combine(destinationDir, entry.FullName));
                    if (!destFilePath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"解压目标路径存在安全风险 (Zip Slip): {entry.FullName}");
                    }

                    string parentFolder = Path.GetDirectoryName(destFilePath);
                    if (!string.IsNullOrEmpty(parentFolder))
                    {
                        Directory.CreateDirectory(parentFolder);
                    }

                    entry.ExtractToFile(destFilePath, overwrite: true);
                }
            }
        }

        /// <summary>
        /// 同步执行漫画归档打包流程。
        /// 根据 Options 的配置扫描待处理文件夹、创建 Zip 归档并可选清理原始文件夹。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public (int Total, int Success) Process(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            // 校验目标路径
            if (options.TargetPaths == null || options.TargetPaths.Count == 0)
            {
                Report(progress, "错误: 未选择任何目标目录。", LogLevel.Error);
                return (0, 0);
            }

            // 校验格式扩展名
            string ext = options.ArchiveType.TrimStart('.').ToLowerInvariant();
            if (ext != "cbz" && ext != "zip")
            {
                Report(progress, $"错误: 不支持的压缩格式 '{options.ArchiveType}'。", LogLevel.Error);
                return (0, 0);
            }

            // 解析额外包含文件的通配符规则并预编译为正则表达式
            var patternStrings = string.IsNullOrWhiteSpace(options.AdditionalIncludePattern)
                ? new[] { "*.xml" }
                : options.AdditionalIncludePattern.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim());

            Regex[] includeRegexes = patternStrings
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled))
                .ToArray();

            List<string> foldersToProcess = new List<string>();

            // 智能搜寻待打包的文件夹
            foreach (var path in options.TargetPaths)
            {
                if (Directory.Exists(path))
                {
                    FindComicFolders(path, foldersToProcess);
                }
            }

            int total = foldersToProcess.Count;
            if (total == 0)
            {
                Report(progress, "提示: 未找到需要压缩的子文件夹。", LogLevel.Warning);
                return (0, 0);
            }

            int maxDegree = Math.Max(1, Math.Min(32, options.MaxDegreeOfParallelism));
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegree,
                CancellationToken = cancellationToken
            };

            Report(progress, $"开始批量打包处理，共 {total} 个文件夹，格式: .{ext}，并发线程数: {maxDegree}", LogLevel.Info);

            int processedCount = 0;
            int successCount = 0;

            try
            {
                Parallel.ForEach(foldersToProcess, parallelOptions, folderPath =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string folderName = Path.GetFileName(folderPath);
                    string parentDir = Path.GetDirectoryName(folderPath) ?? Path.GetPathRoot(folderPath) ?? folderPath;
                    string zipPath = Path.Combine(parentDir, $"{folderName}.{ext}");

                    Report(progress, $"[压缩中] 正在压缩 : {folderName}", LogLevel.Info, processedCount, total, folderName);

                    bool archiveSuccess = false;
                    try
                    {
                        // 若目标压缩包文件已存在则先删除
                        if (File.Exists(zipPath))
                        {
                            File.Delete(zipPath);
                        }

                        // 创建 Zip/CBZ 文件结构
                        CreateZipFromDirectory(folderPath, zipPath, includeRegexes, cancellationToken);
                        archiveSuccess = true;
                    }
                    catch (OperationCanceledException)
                    {
                        try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (Exception e) { Report(progress, $"清理未完成的压缩包失败: {e.Message}", LogLevel.Warning); }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        int currentDone = Interlocked.Increment(ref processedCount);
                        Report(progress, $"[{currentDone:D2}/{total:D2}] 压缩失败 : {ex.Message}", LogLevel.Error, currentDone, total, folderName);
                        try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (Exception e) { Report(progress, $"压缩失败后清理残留文件失败: {e.Message}", LogLevel.Warning); }
                        return;
                    }

                    // 压缩成功且开启了“删除原始文件夹”选项时清理源文件夹
                    if (archiveSuccess && options.DeleteOriginalFolder)
                    {
                        try
                        {
                            ForceDeleteDirectory(folderPath);
                            int currentDone = Interlocked.Increment(ref processedCount);
                            Interlocked.Increment(ref successCount);
                            Report(progress, $"[{currentDone:D2}/{total:D2}] 压缩并清理成功 : {folderName}.{ext}", LogLevel.Success, currentDone, total, folderName);
                        }
                        catch (Exception ex)
                        {
                            int currentDone = Interlocked.Increment(ref processedCount);
                            Interlocked.Increment(ref successCount);
                            Report(progress, $"[{currentDone:D2}/{total:D2}] 压缩成功但清理失败 : {ex.Message}", LogLevel.Warning, currentDone, total, folderName);
                        }
                    }
                    else if (archiveSuccess)
                    {
                        int currentDone = Interlocked.Increment(ref processedCount);
                        Interlocked.Increment(ref successCount);
                        Report(progress, $"[{currentDone:D2}/{total:D2}] 压缩成功 : {folderName}.{ext}", LogLevel.Success, currentDone, total, folderName);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            Report(progress, $"==== 批量打包完成！成功率: {successCount}/{total} ====", LogLevel.Success, processedCount, total);
            return (total, successCount);
        }

        /// <summary>
        /// 从源文件夹遍历文件并生成 Zip/CBZ 归档压缩包。
        /// 默认仅包含图片文件，额外包含匹配规则的文件。
        /// </summary>
        /// <param name="sourceDir">源文件夹路径</param>
        /// <param name="destinationZipPath">生成的 Zip/CBZ 文件目标路径</param>
        /// <param name="includeRegexes">预编译的额外包含正则表达式数组</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        private void CreateZipFromDirectory(
            string sourceDir,
            string destinationZipPath,
            Regex[] includeRegexes,
            CancellationToken cancellationToken)
        {
            var files = SafeGetFiles(sourceDir, "*");

            using (var zipStream = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
            {
                int sourceDirLen = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileName(file);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    bool isImage = ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".bmp" || ext == ".gif";

                    if (!isImage && !IsIncluded(fileName, includeRegexes))
                    {
                        continue;
                    }

                    // 计算在压缩包内的相对路径（统一使用 Unix 风格斜杠 '/'）
                    string relativePath = file.Substring(sourceDirLen).Replace('\\', '/');

                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    using (var entryStream = entry.Open())
                    using (var fileStream = File.OpenRead(file))
                    {
                        fileStream.CopyTo(entryStream);
                    }
                }
            }
        }

        /// <summary>
        /// 智能递归搜寻包含图片的漫画文件夹。
        /// 过滤隐藏、系统及特殊子目录；若存在子文件夹优先递归子文件夹；
        /// 若子目录均未产生漫画归档且当前目录直接包含图片，则回退将当前目录作为单本漫画。
        /// </summary>
        private void FindComicFolders(string dir, List<string> resultList)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                if (!dirInfo.Exists) return;

                var subDirs = dirInfo.GetDirectories()
                    .Where(IsValidScanDirectory)
                    .ToArray();

                if (subDirs.Length > 0)
                {
                    int initialCount = resultList.Count;
                    foreach (var subDir in subDirs)
                    {
                        FindComicFolders(subDir.FullName, resultList);
                    }

                    // 如果所有子目录中均未发现漫画，但当前目录本身直接包含图片，则将当前目录视为单本漫画
                    if (resultList.Count == initialCount && HasImages(dir))
                    {
                        resultList.Add(dir);
                    }
                }
                else
                {
                    // 没有有效子目录时，检查当前文件夹是否包含图片
                    if (HasImages(dir))
                    {
                        resultList.Add(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"智能扫描目录出错 {dir}: {ex.Message}");
            }
        }

        private static bool IsValidScanDirectory(DirectoryInfo dirInfo)
        {
            if (dirInfo == null) return false;
            // 过滤以 . 或 $ 或 @ 开头的特殊/隐藏/系统目录（如 .git, .thumbnails, @eaDir, $RECYCLE.BIN 等）
            if (dirInfo.Name.StartsWith(".") || dirInfo.Name.StartsWith("$") || dirInfo.Name.StartsWith("@"))
                return false;

            // 过滤具有 Hidden, System, ReparsePoint 属性的目录
            if ((dirInfo.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                return false;

            return true;
        }

        private static bool HasImages(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir);
                return files.Any(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".bmp" || ext == ".gif";
                });
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查文件名是否与预编译的额外包含正则表达式数组匹配。
        /// </summary>
        /// <param name="fileName">待校验的文件名</param>
        /// <param name="includeRegexes">预编译的正则表达式数组</param>
        /// <returns>如果匹配（应包含）返回 true，否则返回 false</returns>
        private bool IsIncluded(string fileName, Regex[] includeRegexes)
        {
            foreach (var regex in includeRegexes)
            {
                if (regex.IsMatch(fileName))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 辅助方法：向 IProgress 发送进度和日志状态更新。
        /// </summary>
        private void Report(
            IProgress<ArchiveProgressReport> progress,
            string message,
            LogLevel level,
            int processed = 0,
            int total = 0,
            string folder = null)
        {
            progress?.Report(new ArchiveProgressReport
            {
                Message = message,
                Level = level,
                ProcessedFolders = processed,
                TotalFolders = total,
                CurrentFolder = folder
            });
        }

        private List<string> SafeGetFiles(string path, string searchPattern = "*", int currentDepth = 0, int maxDepth = 20)
        {
            List<string> files = new List<string>();
            if (currentDepth > maxDepth) return files;

            try
            {
                files.AddRange(Directory.GetFiles(path, searchPattern));
                foreach (string directory in Directory.GetDirectories(path))
                {
                    var dirInfo = new DirectoryInfo(directory);
                    if (!IsValidScanDirectory(dirInfo))
                        continue;

                    files.AddRange(SafeGetFiles(directory, searchPattern, currentDepth + 1, maxDepth));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取目录出错 {path}: {ex.Message}");
            }
            return files;
        }

        private void ForceDeleteDirectory(string path, int maxRetries = 4, int delayMs = 150)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (!Directory.Exists(path)) return;

                    var directory = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
                    foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
                    {
                        info.Attributes = FileAttributes.Normal;
                    }
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        System.Diagnostics.Debug.WriteLine($"强行删除文件夹最终失败: {path}, {ex.Message}");
                        throw;
                    }
                    Thread.Sleep(delayMs);
                }
            }
        }
    }
}

