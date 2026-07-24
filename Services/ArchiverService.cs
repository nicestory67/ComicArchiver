using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SevenZip;

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
    /// 批量处理模式枚举。
    /// </summary>
    public enum BatchMode
    {
        /// <summary>
        /// 子文件夹模式：对选定目标目录下的各个子文件夹分别打包压缩为 cbz/zip。
        /// </summary>
        SubFolders,

        /// <summary>
        /// 批量模式：压缩输入目录下的所有目录的子目录。
        /// </summary>
        Batch
    }

    /// <summary>
    /// 归档处理配置参数类。
    /// </summary>
    public class ArchiverOptions
    {
        /// <summary> 目标路径列表（可包含一个或多个文件夹路径） </summary>
        public List<string> TargetPaths { get; set; } = new List<string>();

        /// <summary> 批量打包模式（默认：子文件夹模式） </summary>
        public BatchMode Mode { get; set; } = BatchMode.SubFolders;

        /// <summary> 目标压缩扩展名格式（支持 cbz 或 zip，默认: cbz） </summary>
        public string ArchiveType { get; set; } = "cbz";

        /// <summary> 压缩成功后是否删除原始文件夹（默认: true） </summary>
        public bool DeleteOriginalFolder { get; set; } = true;

        /// <summary> 排除文件的通配符过滤规则（多个格式可用 ';' 或 ',' 分隔，默认: *.db） </summary>
        public string ExcludePattern { get; set; } = "*.db";
    }

    /// <summary>
    /// 漫画文件夹打包与归档核心服务类。
    /// 提供文件夹检索、Zip/CBZ 结构生成、SevenZip CRC32 计算以及源文件夹清理等功能。
    /// </summary>
    public class ArchiverService
    {

        /// <summary>
        /// 异步执行漫画归档打包流程。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public async Task ProcessAsync(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() => Process(options, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 异步执行漫画归档解压流程。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public async Task ExtractAsync(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() => Extract(options, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 同步执行漫画归档解压流程。
        /// 根据 Options 的配置扫描待解压的 CBZ/ZIP 归档、解压至同名文件夹并可选清理原压缩包。
        /// </summary>
        /// <param name="options">归档配置选项</param>
        /// <param name="progress">进度与日志回调接口</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        public void Extract(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options.TargetPaths == null || options.TargetPaths.Count == 0)
            {
                Report(progress, "错误: 未选择任何目标目录。", LogLevel.Error);
                return;
            }

            List<string> archivesToExtract = new List<string>();

            // 根据模式搜寻待解压的压缩包文件
            foreach (var path in options.TargetPaths)
            {
                if (Directory.Exists(path))
                {
                    SearchOption searchOption = options.Mode == BatchMode.Batch
                        ? SearchOption.AllDirectories
                        : SearchOption.TopDirectoryOnly;

                    var files = Directory.GetFiles(path, "*.*", searchOption)
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
                return;
            }

            Report(progress, $"开始批量解压处理，共 {total} 个压缩包文件", LogLevel.Info);

            int processedCount = 0;

            foreach (var archivePath in archivesToExtract)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string archiveName = Path.GetFileName(archivePath);
                string archiveNameWithoutExt = Path.GetFileNameWithoutExtension(archivePath);
                string parentDir = Path.GetDirectoryName(archivePath);
                string targetFolder = Path.Combine(parentDir, archiveNameWithoutExt);

                Report(progress, $"[{processedCount + 1}/{total}] 正在解压: \"{archiveName}\"...", LogLevel.Info, processedCount, total, archiveName);

                bool extractSuccess = false;
                try
                {
                    ExtractArchiveToDirectory(archivePath, targetFolder, cancellationToken);
                    extractSuccess = true;
                    Report(progress, $"  ✓ 已解压至文件夹: \"{archiveNameWithoutExt}\"", LogLevel.Success, processedCount + 1, total, archiveName);
                }
                catch (Exception ex)
                {
                    Report(progress, $"  ✗ 解压失败 \"{archiveName}\": {ex.Message}", LogLevel.Error, processedCount, total, archiveName);
                }

                // 解压成功且开启了“清理原文件”选项时删除原压缩包
                if (extractSuccess && options.DeleteOriginalFolder)
                {
                    try
                    {
                        File.Delete(archivePath);
                        Report(progress, $"  ✓ 已清理原压缩包: \"{archiveName}\"", LogLevel.Info, processedCount + 1, total, archiveName);
                    }
                    catch (Exception ex)
                    {
                        Report(progress, $"  ⚠ 删除原压缩包失败 \"{archiveName}\": {ex.Message}", LogLevel.Warning, processedCount + 1, total, archiveName);
                    }
                }

                processedCount++;
            }

            Report(progress, $"批量解压完成！共成功处理 {processedCount}/{total} 个压缩包。", LogLevel.Success, processedCount, total);
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
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, false))
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
        public void Process(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            // 校验目标路径
            if (options.TargetPaths == null || options.TargetPaths.Count == 0)
            {
                Report(progress, "错误: 未选择任何目标目录。", LogLevel.Error);
                return;
            }

            // 校验格式扩展名
            string ext = options.ArchiveType.TrimStart('.').ToLowerInvariant();
            if (ext != "cbz" && ext != "zip")
            {
                Report(progress, $"错误: 不支持的压缩格式 '{options.ArchiveType}'。", LogLevel.Error);
                return;
            }

            // 解析排除文件的通配符规则
            string[] excludePatterns = string.IsNullOrWhiteSpace(options.ExcludePattern)
                ? new[] { "*.db" }
                : options.ExcludePattern.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim())
                                        .ToArray();

            List<string> foldersToProcess = new List<string>();

            // 根据批量模式搜寻待打包的文件夹
            if (options.Mode == BatchMode.SubFolders)
            {
                foreach (var path in options.TargetPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var subDirs = Directory.GetDirectories(path);
                        foldersToProcess.AddRange(subDirs);
                    }
                }
            }
            else if (options.Mode == BatchMode.Batch)
            {
                foreach (var path in options.TargetPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var level1Dirs = Directory.GetDirectories(path);
                        foreach (var dir1 in level1Dirs)
                        {
                            var level2Dirs = Directory.GetDirectories(dir1);
                            foldersToProcess.AddRange(level2Dirs);
                        }
                    }
                }
            }

            int total = foldersToProcess.Count;
            if (total == 0)
            {
                Report(progress, "提示: 未找到需要压缩的子文件夹。", LogLevel.Warning);
                return;
            }

            Report(progress, $"开始批量打包处理，共 {total} 个文件夹，格式: .{ext}", LogLevel.Info);

            int processedCount = 0;

            // 循环依次压缩处理每个文件夹
            foreach (var folderPath in foldersToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string folderName = Path.GetFileName(folderPath);
                string parentDir = Path.GetDirectoryName(folderPath);
                string zipPath = Path.Combine(parentDir, $"{folderName}.{ext}");

                Report(progress, $"[{processedCount + 1}/{total}] 正在压缩: \"{folderName}\"...", LogLevel.Info, processedCount, total, folderName);

                bool archiveSuccess = false;
                try
                {
                    // 若目标压缩包文件已存在则先删除
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    // 创建 Zip/CBZ 文件结构
                    CreateZipFromDirectory(folderPath, zipPath, excludePatterns, cancellationToken);
                    archiveSuccess = true;
                    Report(progress, $"  ✓ 已生成: \"{folderName}.{ext}\"", LogLevel.Success, processedCount + 1, total, folderName);
                }
                catch (Exception ex)
                {
                    Report(progress, $"  ✗ 压缩失败 \"{folderName}\": {ex.Message}", LogLevel.Error, processedCount, total, folderName);
                }

                // 压缩成功且开启了“删除原始文件夹”选项时清理源文件夹
                if (archiveSuccess && options.DeleteOriginalFolder)
                {
                    try
                    {
                        CleanFiles(folderPath, excludePatterns);
                        Directory.Delete(folderPath, true);
                        Report(progress, $"  ✓ 已清理原始文件夹: \"{folderName}\"", LogLevel.Info, processedCount + 1, total, folderName);
                    }
                    catch (Exception ex)
                    {
                        Report(progress, $"  ⚠ 删除原始文件夹失败 \"{folderName}\": {ex.Message}", LogLevel.Warning, processedCount + 1, total, folderName);
                    }
                }

                processedCount++;
            }

            Report(progress, $"批量打包完成！共成功处理 {processedCount}/{total} 个文件夹。", LogLevel.Success, processedCount, total);
        }

        /// <summary>
        /// 从源文件夹遍历文件并生成 Zip/CBZ 归档压缩包。
        /// 过滤符合排除通配符的文件，并调用 SevenZip 计算 CRC32 值。
        /// </summary>
        /// <param name="sourceDir">源文件夹路径</param>
        /// <param name="destinationZipPath">生成的 Zip/CBZ 文件目标路径</param>
        /// <param name="excludePatterns">排除通配符数组</param>
        /// <param name="cancellationToken">取消操作令牌</param>
        private void CreateZipFromDirectory(
            string sourceDir,
            string destinationZipPath,
            string[] excludePatterns,
            CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            using (var zipStream = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
            {
                int sourceDirLen = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fileName = Path.GetFileName(file);
                    if (IsExcluded(fileName, excludePatterns))
                    {
                        continue;
                    }

                    // 计算在压缩包内的相对路径（统一使用 Unix 风格斜杠 '/'）
                    string relativePath = file.Substring(sourceDirLen).Replace('\\', '/');

                    // 利用 7z LZMA SDK 计算文件的 CRC32 校验码
                    uint crc = CalculateFileCrc32(file);

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
        /// 使用 7-Zip (LZMA SDK) 的 CRC 类计算文件的 CRC32 校验和。
        /// </summary>
        /// <param name="file">需要计算 CRC32 的文件路径</param>
        /// <returns>CRC32 无符号 32 位整数；若读取发生异常返回 0</returns>
        private uint CalculateFileCrc32(string file)
        {
            try
            {
                var crc = new CRC();
                crc.Init();
                using (var fs = File.OpenRead(file))
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        crc.Update(buffer, 0, (uint)bytesRead);
                    }
                }
                return crc.GetDigest();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 在删除原始文件夹之前清理匹配排除规则的文件（如清除 .db 等系统缩略图文件）。
        /// </summary>
        /// <param name="dir">要清理的目标文件夹</param>
        /// <param name="patterns">通配符规则数组</param>
        private void CleanFiles(string dir, string[] patterns)
        {
            try
            {
                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 检查文件名是否与通配符规则匹配（例如将 "*.db" 转换为正则表达式匹配）。
        /// </summary>
        /// <param name="fileName">待校验的文件名</param>
        /// <param name="patterns">通配符模式数组</param>
        /// <returns>如果匹配（应排除）返回 true，否则返回 false</returns>
        private bool IsExcluded(string fileName, string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase))
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
    }
}

