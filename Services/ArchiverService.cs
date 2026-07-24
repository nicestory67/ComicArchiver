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
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class ArchiveProgressReport
    {
        public string Message { get; set; }
        public LogLevel Level { get; set; } = LogLevel.Info;
        public int ProcessedFolders { get; set; }
        public int TotalFolders { get; set; }
        public double Percentage => TotalFolders > 0 ? (double)ProcessedFolders / TotalFolders * 100 : 0;
        public string CurrentFolder { get; set; }
    }

    public enum BatchMode
    {
        /// <summary>
        /// 将指定目录下的各个子文件夹分别压缩为 cbz/zip (与原 bat 逻辑一致)
        /// </summary>
        SubFolders,

        /// <summary>
        /// 直接将选定的每个文件夹自身压缩为对应后缀 (适合多选拖入或批量多文件夹)
        /// </summary>
        DirectFolders
    }

    public class ArchiverOptions
    {
        public List<string> TargetPaths { get; set; } = new List<string>();
        public BatchMode Mode { get; set; } = BatchMode.SubFolders;
        public string ArchiveType { get; set; } = "cbz";
        public bool DeleteOriginalFolder { get; set; } = true;
        public string ExcludePattern { get; set; } = "*.db";
    }

    public class ArchiverService
    {
        private static readonly HashSet<string> ProtectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".vs", ".git", ".agents", "services", "lzma2602", "properties"
        };

        public async Task ProcessAsync(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() => Process(options, progress, cancellationToken), cancellationToken);
        }

        public void Process(
            ArchiverOptions options,
            IProgress<ArchiveProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options.TargetPaths == null || options.TargetPaths.Count == 0)
            {
                Report(progress, "错误: 未选择任何目标目录。", LogLevel.Error);
                return;
            }

            string ext = options.ArchiveType.TrimStart('.').ToLowerInvariant();
            if (ext != "cbz" && ext != "zip")
            {
                Report(progress, $"错误: 不支持的压缩格式 '{options.ArchiveType}'。", LogLevel.Error);
                return;
            }

            string[] excludePatterns = string.IsNullOrWhiteSpace(options.ExcludePattern)
                ? new[] { "*.db" }
                : options.ExcludePattern.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim())
                                        .ToArray();

            List<string> foldersToProcess = new List<string>();

            if (options.Mode == BatchMode.SubFolders)
            {
                foreach (var path in options.TargetPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var subDirs = Directory.GetDirectories(path)
                            .Where(d => !ProtectedFolders.Contains(Path.GetFileName(d)))
                            .ToArray();
                        foldersToProcess.AddRange(subDirs);
                    }
                }
            }
            else
            {
                foreach (var path in options.TargetPaths)
                {
                    if (Directory.Exists(path) && !ProtectedFolders.Contains(Path.GetFileName(path)))
                    {
                        foldersToProcess.Add(path);
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
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    CreateZipFromDirectory(folderPath, zipPath, excludePatterns, cancellationToken);
                    archiveSuccess = true;
                    Report(progress, $"  ✓ 已生成: \"{folderName}.{ext}\"", LogLevel.Success, processedCount + 1, total, folderName);
                }
                catch (Exception ex)
                {
                    Report(progress, $"  ✗ 压缩失败 \"{folderName}\": {ex.Message}", LogLevel.Error, processedCount, total, folderName);
                }

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

                    string relativePath = file.Substring(sourceDirLen).Replace('\\', '/');

                    // Calculate CRC32 using 7z LZMA SDK
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
