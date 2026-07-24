using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComicArchiver.Services;
using HandyControl.Controls;

namespace ComicArchiver
{
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private CancellationTokenSource _cts;
        private readonly ArchiverService _archiverService;
        private List<string> _selectedPaths = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            _archiverService = new ArchiverService();
            SetTargetPath(Environment.CurrentDirectory);
            AddLog("ComicArchiver 已完成纯动态资源主题替换。", LogLevel.Info);
        }

        private void SetTargetPath(params string[] paths)
        {
            _selectedPaths.Clear();
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    if (Directory.Exists(p))
                    {
                        _selectedPaths.Add(p);
                    }
                    else if (File.Exists(p))
                    {
                        string dir = Path.GetDirectoryName(p);
                        if (!string.IsNullOrEmpty(dir) && !_selectedPaths.Contains(dir))
                        {
                            _selectedPaths.Add(dir);
                        }
                    }
                }
            }

            if (_selectedPaths.Count == 0)
            {
                TxtTargetDir.Text = string.Empty;
                TxtDragHint.Text = "💡 提示：请拖入文件夹或点击 [浏览...] 选择";
            }
            else if (_selectedPaths.Count == 1)
            {
                TxtTargetDir.Text = _selectedPaths[0];
                TxtDragHint.Text = RbModeSubFolders.IsChecked == true
                    ? "💡 子目录模式：将压缩该目录下的每一个子文件夹"
                    : "💡 直压模式：将直接压缩该文件夹本身";
            }
            else
            {
                TxtTargetDir.Text = $"[批量模式] 已选择 {_selectedPaths.Count} 个文件夹";
                TxtDragHint.Text = $"💡 批量直压模式：将对选中的 {_selectedPaths.Count} 个文件夹分别打包";
                RbModeDirect.IsChecked = true;
            }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtDragHint == null) return;
            if (_selectedPaths.Count > 1)
            {
                TxtDragHint.Text = $"💡 批量直压模式：将对选中的 {_selectedPaths.Count} 个文件夹分别打包";
            }
            else
            {
                TxtDragHint.Text = RbModeSubFolders.IsChecked == true
                    ? "💡 子目录模式：将压缩选定目录下的每一个子文件夹"
                    : "💡 文件夹直压模式：将直接压缩选定的文件夹本身";
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "请选择要打包的目标文件夹";
                if (_selectedPaths.Count > 0)
                {
                    dialog.SelectedPath = _selectedPaths[0];
                }
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SetTargetPath(dialog.SelectedPath);
                }
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPaths.Count <= 1)
            {
                string manualText = TxtTargetDir.Text?.Trim();
                if (!string.IsNullOrEmpty(manualText) && Directory.Exists(manualText))
                {
                    _selectedPaths = new List<string> { manualText };
                }
            }

            if (_selectedPaths.Count == 0)
            {
                HandyControl.Controls.MessageBox.Show("请选择有效的目标文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var options = new ArchiverOptions
            {
                TargetPaths = new List<string>(_selectedPaths),
                Mode = RbModeSubFolders.IsChecked == true ? BatchMode.SubFolders : BatchMode.DirectFolders,
                ArchiveType = RbCbz.IsChecked == true ? "cbz" : "zip",
                DeleteOriginalFolder = ChkDeleteOriginal.IsChecked == true,
                ExcludePattern = TxtExclude.Text?.Trim()
            };

            SetUiRunningState(true);
            _cts = new CancellationTokenSource();

            var progress = new Progress<ArchiveProgressReport>(report =>
            {
                if (!string.IsNullOrEmpty(report.Message))
                {
                    AddLog(report.Message, report.Level);
                }

                if (report.TotalFolders > 0)
                {
                    ProgBar.Value = report.Percentage;
                    TxtPercentage.Text = $"{report.Percentage:F0}%";
                    TxtStatus.Text = $"处理中 ({report.ProcessedFolders}/{report.TotalFolders}): {report.CurrentFolder}";
                }
            });

            try
            {
                AddLog($"🚀 开始批量打包任务...", LogLevel.Info);
                await _archiverService.ProcessAsync(options, progress, _cts.Token);
                ProgBar.Value = 100;
                TxtPercentage.Text = "100%";
                TxtStatus.Text = "任务完成！";
                TxtSummary.Text = "所有文件夹处理完成。";
            }
            catch (OperationCanceledException)
            {
                AddLog("⚠ 用户已取消操作。", LogLevel.Warning);
                TxtStatus.Text = "已取消";
            }
            catch (Exception ex)
            {
                AddLog($"❌ 出错: {ex.Message}", LogLevel.Error);
                TxtStatus.Text = "处理失败";
            }
            finally
            {
                SetUiRunningState(false);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnCancel.IsEnabled = false;
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string path = _selectedPaths.FirstOrDefault();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                path = TxtTargetDir.Text?.Trim();
            }

            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                HandyControl.Controls.MessageBox.Show("无法打开，目录不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (dropped != null && dropped.Length > 0)
                {
                    SetTargetPath(dropped);
                    AddLog($"已通过拖拽载入 {dropped.Length} 个目标项", LogLevel.Info);
                }
            }
        }

        private void SetUiRunningState(bool isRunning)
        {
            BtnStart.IsEnabled = !isRunning;
            BtnCancel.IsEnabled = isRunning;
            BtnBrowse.IsEnabled = !isRunning;
            TxtTargetDir.IsEnabled = !isRunning;
            RbModeSubFolders.IsEnabled = !isRunning;
            RbModeDirect.IsEnabled = !isRunning;
            RbCbz.IsEnabled = !isRunning;
            RbZip.IsEnabled = !isRunning;
            ChkDeleteOriginal.IsEnabled = !isRunning;
            TxtExclude.IsEnabled = !isRunning;

            if (isRunning)
            {
                ProgBar.Value = 0;
                TxtPercentage.Text = "0%";
            }
        }

        private void AddLog(string message, LogLevel level)
        {
            Brush brush;
            switch (level)
            {
                case LogLevel.Success:
                    brush = (Brush)TryFindResource("SuccessBrush") ?? Brushes.Green;
                    break;
                case LogLevel.Warning:
                    brush = (Brush)TryFindResource("WarningBrush") ?? Brushes.Yellow;
                    break;
                case LogLevel.Error:
                    brush = (Brush)TryFindResource("DangerBrush") ?? Brushes.Red;
                    break;
                default:
                    brush = (Brush)TryFindResource("PrimaryTextBrush") ?? Brushes.White;
                    break;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var item = new ListBoxItem
            {
                Content = $"[{timestamp}] {message}",
                Foreground = brush
            };

            LstLogs.Items.Add(item);
            LstLogs.ScrollIntoView(item);
        }
    }
}
