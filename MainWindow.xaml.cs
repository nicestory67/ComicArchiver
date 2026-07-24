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
    /// <summary>
    /// MainWindow.xaml 的交互逻辑。
    /// WPF 核心 UI 主窗口，负责展示处理进度、控制按钮、批量拖拽响应以及日志交互。
    /// </summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        /// <summary> 用于支持异步打包任务的中途取消 CancellationTokenSource 实例 </summary>
        private CancellationTokenSource _cts;

        /// <summary> 漫画归档打包核心服务对象 </summary>
        private readonly ArchiverService _archiverService;

        /// <summary> 当前选中的目标文件夹路径列表 </summary>
        private List<string> _selectedPaths = new List<string>();

        /// <summary>
        /// 初始化 MainWindow 窗口实例。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            _archiverService = new ArchiverService();
            SetTargetPath();

            // 默认设置为 HandyControl 暗色主题
            HandyControl.Themes.Theme.SetSkin(this, HandyControl.Data.SkinType.Dark);
        }

        /// <summary>
        /// 设置并刷新当前目标路径列表，同时动态更新 UI 的提示信息文本与压缩模式单选框状态。
        /// </summary>
        /// <param name="paths">一个或多个目标文件/文件夹路径</param>
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
                        string ext = Path.GetExtension(p)?.ToLowerInvariant();
                        if ((ext == ".cbz" || ext == ".zip") && !_selectedPaths.Contains(p))
                        {
                            _selectedPaths.Add(p);
                        }
                        else
                        {
                            // 若拖入/选择了普通文件，则提取其所在的父目录
                            string dir = Path.GetDirectoryName(p);
                            if (!string.IsNullOrEmpty(dir) && !_selectedPaths.Contains(dir))
                            {
                                _selectedPaths.Add(dir);
                            }
                        }
                    }
                }
            }

            if (_selectedPaths.Count == 0)
            {
                TxtTargetDir.Text = string.Empty;
                TxtDragHint.Text = "提示：请拖入文件夹或点击 [浏览...] 选择";
            }
            else if (_selectedPaths.Count == 1)
            {
                TxtTargetDir.Text = _selectedPaths[0];
                TxtDragHint.Text = RbModeSubFolders.IsChecked == true
                    ? "子目录模式：将压缩该目录下的每一个子文件夹"
                    : "批量模式：将压缩该目录下所有目录的子文件夹";
            }
            else
            {
                TxtTargetDir.Text = $"[已选择多项] 已选择 {_selectedPaths.Count} 个文件夹";
                TxtDragHint.Text = RbModeSubFolders.IsChecked == true
                    ? $"子目录模式：将分别压缩选中的 {_selectedPaths.Count} 个目录下的每一个子文件夹"
                    : $"批量模式：将压缩选中的 {_selectedPaths.Count} 个目录下所有目录的子文件夹";
            }
        }

        /// <summary>
        /// 打包模式单选框变更事件响应函数，用于即时更新提示说明。
        /// </summary>
        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtDragHint == null) return;
            if (_selectedPaths.Count > 1)
            {
                TxtDragHint.Text = RbModeSubFolders.IsChecked == true
                    ? $"子目录模式：将分别压缩选中的 {_selectedPaths.Count} 个目录下的每一个子文件夹"
                    : $"批量模式：将压缩选中的 {_selectedPaths.Count} 个目录下所有目录的子文件夹";
            }
            else
            {
                TxtDragHint.Text = RbModeSubFolders.IsChecked == true
                    ? "子目录模式：将压缩选定目录下的每一个子文件夹"
                    : "批量模式：将压缩选定目录下所有目录的子文件夹";
            }
        }

        /// <summary>
        /// 点击 [浏览...] 按钮事件，使用 Windows Forms 的 FolderBrowserDialog 选取目标目录。
        /// </summary>
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

        /// <summary>
        /// 点击 [开始解压] 按钮事件，组装解压配置并开启后台异步解压任务。
        /// </summary>
        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            // 校验手动在文本框中输入的路径
            if (_selectedPaths.Count <= 1)
            {
                string manualText = TxtTargetDir.Text?.Trim();
                if (!string.IsNullOrEmpty(manualText) && (Directory.Exists(manualText) || File.Exists(manualText)))
                {
                    _selectedPaths = new List<string> { manualText };
                }
            }

            if (_selectedPaths.Count == 0)
            {
                HandyControl.Controls.MessageBox.Show("请选择有效的目标文件夹或压缩包！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 构造解压参数实例
            var options = new ArchiverOptions
            {
                TargetPaths = new List<string>(_selectedPaths),
                Mode = RbModeSubFolders.IsChecked == true ? BatchMode.SubFolders : BatchMode.Batch,
                ArchiveType = RbCbz.IsChecked == true ? "cbz" : "zip",
                DeleteOriginalFolder = ChkDeleteOriginal.IsChecked == true,
                ExcludePattern = TxtExclude.Text?.Trim()
            };

            SetUiRunningState(true);
            _cts = new CancellationTokenSource();

            // 进度汇报与 UI 刷新句柄
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
                    TxtStatus.Text = $"解压中 ({report.ProcessedFolders}/{report.TotalFolders}): {report.CurrentFolder}";
                }
            });

            try
            {
                AddLog($"📦 开始批量解压任务...", LogLevel.Info);
                await _archiverService.ExtractAsync(options, progress, _cts.Token);
                ProgBar.Value = 100;
                TxtPercentage.Text = "100%";
                TxtStatus.Text = "解压完成！";
                TxtSummary.Text = "所有压缩包解压完成。";
            }
            catch (OperationCanceledException)
            {
                AddLog("⚠ 用户已取消操作。", LogLevel.Warning);
                TxtStatus.Text = "已取消";
            }
            catch (Exception ex)
            {
                AddLog($"❌ 解压出错: {ex.Message}", LogLevel.Error);
                TxtStatus.Text = "解压失败";
            }
            finally
            {
                SetUiRunningState(false);
            }
        }

        /// <summary>
        /// 点击 [开始打包] 按钮事件，组装打包配置并开启后台异步任务。
        /// </summary>
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // 校验手动在文本框中输入的路径
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

            // 构造打包参数实例
            var options = new ArchiverOptions
            {
                TargetPaths = new List<string>(_selectedPaths),
                Mode = RbModeSubFolders.IsChecked == true ? BatchMode.SubFolders : BatchMode.Batch,
                ArchiveType = RbCbz.IsChecked == true ? "cbz" : "zip",
                DeleteOriginalFolder = ChkDeleteOriginal.IsChecked == true,
                ExcludePattern = TxtExclude.Text?.Trim()
            };

            SetUiRunningState(true);
            _cts = new CancellationTokenSource();

            // 进度汇报与 UI 刷新句柄
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

        /// <summary>
        /// 点击 [取消] 按钮事件，触发取消令牌中止正在进行的打包任务。
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnCancel.IsEnabled = false;
        }

        /// <summary>
        /// 点击 [打开目标文件夹] 按钮事件，启动 Explorer 进程定位当前选定的目录。
        /// </summary>
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

        /// <summary>
        /// 拖拽移入窗口事件响应，验证鼠标悬停包含文件/文件夹拖拽数据。
        /// </summary>
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

        /// <summary>
        /// 拖拽放置文件/文件夹放置松开鼠标事件响应，提取拖拽路径并设为当前目标。
        /// </summary>
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

        /// <summary>
        /// 控制 UI 控件在“运行中”与“空闲中”状态切换时的可操作性（禁用/启用）。
        /// </summary>
        /// <param name="isRunning">当前是否正在运行打包任务</param>
        private void SetUiRunningState(bool isRunning)
        {
            BtnStart.IsEnabled = !isRunning;
            BtnExtract.IsEnabled = !isRunning;
            BtnCancel.IsEnabled = isRunning;
            BtnBrowse.IsEnabled = !isRunning;
            TxtTargetDir.IsEnabled = !isRunning;
            RbModeSubFolders.IsEnabled = !isRunning;
            RbModeBatch.IsEnabled = !isRunning;
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

        /// <summary>
        /// 向日志列表框添加一条附带时间戳和配色样式的日志记录，并自动滚动至最底部。
        /// </summary>
        /// <param name="message">日志文本内容</param>
        /// <param name="level">日志等级类型</param>
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

        /// <summary>
        /// 点击 [ToggleThemeCommand] 按钮事件，切换 HandyControl 的 SkinDefault（明亮）与 SkinDark（暗黑）主题。
        /// </summary>
        private void ToggleThemeCommand_Click(object sender, RoutedEventArgs e)
        {
            bool isDark = ToggleThemeCommand.IsChecked == true;
            var skinType = isDark ? HandyControl.Data.SkinType.Dark : HandyControl.Data.SkinType.Default;

            // 1. 使用 HandyControl API 设置当前窗口主题
            HandyControl.Themes.Theme.SetSkin(this, skinType);

            // 2. 动态递归替换全局资源字典中的皮肤 (SkinDefault.xaml <-> SkinDark.xaml)
            var newSkinUri = new Uri(isDark
                ? "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml"
                : "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml");

            ReplaceSkinInDictionary(Application.Current.Resources, newSkinUri);
        }

        /// <summary>
        /// 递归查找并替换 ResourceDictionary 中的 HandyControl 皮肤字典 (SkinDefault / SkinDark)。
        /// </summary>
        private void ReplaceSkinInDictionary(ResourceDictionary dict, Uri newSkinUri)
        {
            if (dict == null) return;

            var skinToReplace = dict.MergedDictionaries.FirstOrDefault(d =>
                d.Source != null && (d.Source.OriginalString.Contains("SkinDefault.xaml") || d.Source.OriginalString.Contains("SkinDark.xaml")));

            if (skinToReplace != null)
            {
                int index = dict.MergedDictionaries.IndexOf(skinToReplace);
                dict.MergedDictionaries.RemoveAt(index);
                dict.MergedDictionaries.Insert(index, new ResourceDictionary { Source = newSkinUri });
            }
            else
            {
                foreach (var childDict in dict.MergedDictionaries)
                {
                    ReplaceSkinInDictionary(childDict, newSkinUri);
                }
            }
        }
    }
}

