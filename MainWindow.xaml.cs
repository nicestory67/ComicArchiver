using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
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
        private bool _isThemeInitializing = true;
        private bool _isContextMenuInitializing = true;
        private const string ThemeSettingsKey = @"Software\ComicArchiver\Settings";

        /// <summary> 用于支持异步打包任务的中途取消 CancellationTokenSource 实例 </summary>
        private CancellationTokenSource _cts;

        /// <summary> 漫画归档打包核心服务对象 </summary>
        private readonly ArchiverService _archiverService;

        /// <summary> 当前选中的目标文件夹路径列表 </summary>
        private List<string> _selectedPaths = new List<string>();

        private enum AutoRunAction { None, Pack, Extract }
        private AutoRunAction _autoRunAction = AutoRunAction.None;

        private bool _isAutoRun = false;
        private string _autoRunTarget = null;
        private bool _isProcessing = false;

        /// <summary>
        /// 初始化 MainWindow 窗口实例。
        /// </summary>
        public MainWindow() : this(Environment.GetCommandLineArgs().Skip(1).ToArray())
        {
        }

        /// <summary>
        /// 初始化 MainWindow 窗口实例，并载入指定的目标路径。
        /// </summary>
        /// <param name="args">命令行传入的参数数组</param>
        public MainWindow(params string[] args)
        {
            InitializeComponent();
            _archiverService = new ArchiverService();

            // 读取内置配置并应用主题
            bool isDark = LoadThemeSetting();
            ToggleThemeCommand.IsChecked = isDark;
            ApplyTheme(isDark);
            _isThemeInitializing = false;

            // 解析参数进行自动运行配置
            List<string> paths = new List<string>();
            if (args != null && args.Length > 0)
            {
                foreach (var arg in args)
                {
                    if (arg.StartsWith("/autorun:pack", StringComparison.OrdinalIgnoreCase) || 
                        arg.StartsWith("/autorun:subfolders", StringComparison.OrdinalIgnoreCase) ||
                        arg.StartsWith("/autorun:batch", StringComparison.OrdinalIgnoreCase))
                    {
                        _isAutoRun = true;
                        _autoRunAction = AutoRunAction.Pack;
                    }
                    else if (arg.StartsWith("/autorun:extract", StringComparison.OrdinalIgnoreCase))
                    {
                        _isAutoRun = true;
                        _autoRunAction = AutoRunAction.Extract;
                    }
                    else
                    {
                        paths.Add(arg);
                    }
                }
            }

            _ = SetTargetPathAsync(paths.ToArray());

            if (_isAutoRun && paths.Count > 0)
            {
                _autoRunTarget = paths[0];
                this.Loaded += MainWindow_Loaded;
            }

            // 初始化检测右键菜单状态
            ChkContextMenu.IsChecked = CheckContextMenuStatus();
            _isContextMenuInitializing = false;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= MainWindow_Loaded;

            if (_isAutoRun)
            {
                string actionName = _autoRunAction == AutoRunAction.Extract ? "解压" : "打包";
                AddLog($"进入自动运行模式，即将开始{actionName}...", LogLevel.Info);
                SetUiRunningState(true);
                await Task.Delay(1000);
                SetUiRunningState(false);
                
                if (_autoRunAction == AutoRunAction.Extract)
                {
                    BtnExtract_Click(null, null);
                }
                else
                {
                    BtnStart_Click(null, null);
                }
            }
        }

        /// <summary>
        /// 设置并刷新当前目标路径列表，同时动态更新 UI 的提示信息文本与压缩模式单选框状态。
        /// </summary>
        /// <param name="paths">一个或多个目标文件/文件夹路径</param>
        private async Task SetTargetPathAsync(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                _selectedPaths.Clear();
                TxtTargetDir.Text = string.Empty;
                TxtDragHint.Text = "提示：请拖入文件夹或点击 [浏览...] 选择";
                return;
            }

            var validPaths = new List<string>();

            foreach (var p in paths)
            {
                bool exists = await Task.Run(() => Directory.Exists(p) || File.Exists(p));
                if (exists)
                {
                    validPaths.Add(p);
                    break; // 仅支持单一文件夹目标
                }
            }

            _selectedPaths.Clear();
            _selectedPaths.AddRange(validPaths);

            if (_selectedPaths.Count == 0)
            {
                TxtTargetDir.Text = string.Empty;
                TxtDragHint.Text = "提示：请拖入文件夹或点击 [浏览...] 选择";
            }
            else
            {
                TxtTargetDir.Text = _selectedPaths[0];
                if (Directory.Exists(_selectedPaths[0]))
                {
                    TxtDragHint.Text = "智能模式：将自动扫描图片特征并独立打包";
                }
                else
                {
                    TxtDragHint.Text = "提示：已选择单文件，仅适用解压";
                }
            }
            await Task.CompletedTask;
        }

        // 已移除 Mode_Changed

        /// <summary>
        /// 当输入框内容被清空时，同步清理后台记录的待处理目录。
        /// </summary>
        private void TxtTargetDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTargetDir.Text))
            {
                _selectedPaths.Clear();
                TxtDragHint.Text = "提示：请拖入文件夹或点击 [浏览...] 选择";
            }
        }

        /// <summary>
        /// 点击 [浏览...] 按钮事件，使用 Ookii.Dialogs.Wpf 的 VistaFolderBrowserDialog 选取目标目录。
        /// </summary>
        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
            {
                Description = "请选择要打包的目标文件夹",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (_selectedPaths.Count > 0)
            {
                dialog.SelectedPath = _selectedPaths[0];
            }

            if (dialog.ShowDialog(this) == true)
            {
                await SetTargetPathAsync(dialog.SelectedPath);
                if (_selectedPaths.Count > 0)
                {
                    AddLog($"待处理目录: {new DirectoryInfo(_selectedPaths[0]).Name}", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// 点击 [开始解压] 按钮事件，组装解压配置并开启后台异步解压任务。
        /// </summary>
        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            BtnExtract.IsChecked = true;

            // 校验手动在文本框中输入的路径
            if (_selectedPaths.Count <= 1)
            {
                string manualText = TxtTargetDir.Text?.Trim();
                if (!string.IsNullOrEmpty(manualText))
                {
                    bool exists = await Task.Run(() => Directory.Exists(manualText) || File.Exists(manualText));
                    if (exists)
                    {
                        _selectedPaths = new List<string> { manualText };
                    }
                }
            }

            if (_selectedPaths.Count == 0)
            {
                string manualText = TxtTargetDir.Text?.Trim();
                if (!string.IsNullOrEmpty(manualText))
                {
                    HandyControl.Controls.MessageBox.Show("您输入的路径不存在或无法访问，请检查后重试！", "路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    HandyControl.Controls.MessageBox.Show("请选择有效的目标文件夹或压缩包！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                _isProcessing = false;
                return;
            }

            // 构造解压参数实例
            var options = new ArchiverOptions
            {
                TargetPaths = new List<string>(_selectedPaths),
                ArchiveType = RbCbz.IsChecked == true ? "cbz" : "zip",
                DeleteOriginalFolder = ChkDeleteOriginal.IsChecked == true,
                AdditionalIncludePattern = TxtAdditionalInclude.Text?.Trim()
            };

            SetUiRunningState(true);
            _cts?.Dispose();
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
                    BtnExtract.Progress = report.Percentage;
                    TxtStatus.Text = $"解压中 ({report.ProcessedFolders}/{report.TotalFolders}): {report.CurrentFolder}";
                }
            });

            bool hasError = false;

            try
            {
                AddLog($"开始批量解压任务...", LogLevel.Info);
                var result = await _archiverService.ExtractAsync(options, progress, _cts.Token);
                if (result.Total == 0)
                {
                    TxtStatus.Text = "无解压任务";
                }
                else if (result.Success == 0)
                {
                    TxtStatus.Text = "全部失败";
                    hasError = true;
                }
                else if (result.Success < result.Total)
                {
                    BtnExtract.Progress = 100;
                    TxtStatus.Text = $"部分完成 ({result.Success}/{result.Total})";
                    hasError = true;
                }
                else
                {
                    BtnExtract.Progress = 100;
                    TxtStatus.Text = "解压完成！";
                }
            }
            catch (OperationCanceledException)
            {
                hasError = true;
                AddLog("用户已取消操作。", LogLevel.Warning);
                TxtStatus.Text = "已取消";
            }
            catch (Exception ex)
            {
                hasError = true;
                AddLog($"解压出错: {ex.Message}", LogLevel.Error);
                TxtStatus.Text = "解压失败";
                HandyControl.Controls.MessageBox.Show($"解压过程中发生错误：\n{ex.Message}", "解压失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnExtract.IsChecked = false;
                SetUiRunningState(false);
                _cts?.Dispose();
                _cts = null;
                _isProcessing = false;
            }

            if (_isAutoRun)
            {
                if (!hasError)
                {
                    // 防止在自动退出前被再次点击
                    BtnStart.IsHitTestVisible = false;
                    BtnExtract.IsHitTestVisible = false;
                    BtnCancel.IsEnabled = false;

                    AddLog("处理完毕，即将自动退出...", LogLevel.Info);
                    await Task.Delay(1000);
                    Application.Current.Shutdown();
                }
                else
                {
                    AddLog("自动运行过程中出现警告或取消，退出自动模式。", LogLevel.Warning);
                    _isAutoRun = false;
                }
            }
        }

        /// <summary>
        /// 点击 [开始打包] 按钮事件，组装打包配置并开启后台异步任务。
        /// </summary>
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            BtnStart.IsChecked = true;

            // 校验手动在文本框中输入的路径
            if (_selectedPaths.Count <= 1)
            {
                string manualText = TxtTargetDir.Text?.Trim();
                if (!string.IsNullOrEmpty(manualText))
                {
                    bool exists = await Task.Run(() => Directory.Exists(manualText));
                    if (exists)
                    {
                        _selectedPaths = new List<string> { manualText };
                    }
                }
            }

            if (_selectedPaths.Count == 0)
            {
                string manualText = TxtTargetDir.Text?.Trim();
                if (!string.IsNullOrEmpty(manualText))
                {
                    HandyControl.Controls.MessageBox.Show("您输入的路径不存在或无法访问，请检查后重试！", "路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    HandyControl.Controls.MessageBox.Show("请选择有效的目标文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                _isProcessing = false;
                return;
            }

            // 构造打包参数实例
            var options = new ArchiverOptions
            {
                TargetPaths = new List<string>(_selectedPaths),
                ArchiveType = RbCbz.IsChecked == true ? "cbz" : "zip",
                DeleteOriginalFolder = ChkDeleteOriginal.IsChecked == true,
                AdditionalIncludePattern = TxtAdditionalInclude.Text?.Trim()
            };

            SetUiRunningState(true);
            _cts?.Dispose();
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
                    BtnStart.Progress = report.Percentage;
                    TxtStatus.Text = $"处理中 ({report.ProcessedFolders}/{report.TotalFolders}): {report.CurrentFolder}";
                }
            });

            bool hasError = false;

            try
            {
                AddLog($"开始批量打包任务...", LogLevel.Info);
                var result = await _archiverService.ProcessAsync(options, progress, _cts.Token);
                if (result.Total == 0)
                {
                    TxtStatus.Text = "无打包任务";
                }
                else if (result.Success == 0)
                {
                    TxtStatus.Text = "全部失败";
                    hasError = true;
                }
                else if (result.Success < result.Total)
                {
                    BtnStart.Progress = 100;
                    TxtStatus.Text = $"部分完成 ({result.Success}/{result.Total})";
                    hasError = true;
                }
                else
                {
                    BtnStart.Progress = 100;
                    TxtStatus.Text = "任务完成！";
                }
            }
            catch (OperationCanceledException)
            {
                hasError = true;
                AddLog("用户已取消操作。", LogLevel.Warning);
                TxtStatus.Text = "已取消";
            }
            catch (Exception ex)
            {
                hasError = true;
                AddLog($"出错: {ex.Message}", LogLevel.Error);
                TxtStatus.Text = "处理失败";
                HandyControl.Controls.MessageBox.Show($"打包过程中发生错误：\n{ex.Message}", "打包失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnStart.IsChecked = false;
                SetUiRunningState(false);
                _cts?.Dispose();
                _cts = null;
                _isProcessing = false;
            }

            if (_isAutoRun)
            {
                if (!hasError)
                {
                    // 防止在自动退出前被再次点击
                    BtnStart.IsHitTestVisible = false;
                    BtnExtract.IsHitTestVisible = false;
                    BtnCancel.IsEnabled = false;

                    AddLog("处理完毕，即将自动退出...", LogLevel.Info);
                    await Task.Delay(1000);
                    Application.Current.Shutdown();
                }
                else
                {
                    AddLog("自动运行过程中出现警告或取消，退出自动模式。", LogLevel.Warning);
                    _isAutoRun = false;
                }
            }
        }

        private const string ContextMenuKey = @"Software\Classes\Directory\shell\ComicArchiver";

        private bool CheckContextMenuStatus()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ContextMenuKey))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ChkContextMenu_Checked(object sender, RoutedEventArgs e)
        {
            if (_isContextMenuInitializing) return;

            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKey, false);
                using (var key = Registry.CurrentUser.CreateSubKey(ContextMenuKey))
                {
                    key.SetValue("MUIVerb", "使用 ComicArchiver 处理");
                    key.SetValue("Icon", $"\"{exePath}\",0");
                    key.SetValue("SubCommands", "");

                    using (var subKey = key.CreateSubKey(@"shell\01_pack"))
                    {
                        subKey.SetValue("MUIVerb", "智能打包 (自动扫描)");
                        using (var cmdKey = subKey.CreateSubKey("command"))
                        {
                            cmdKey.SetValue("", $"\"{exePath}\" \"%1\" /autorun:pack");
                        }
                    }

                    using (var subKey = key.CreateSubKey(@"shell\02_extract"))
                    {
                        subKey.SetValue("MUIVerb", "一键解压 (向下递归)");
                        subKey.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord); // 0x20 = 分隔线 (前置)
                        using (var cmdKey = subKey.CreateSubKey("command"))
                        {
                            cmdKey.SetValue("", $"\"{exePath}\" \"%1\" /autorun:extract");
                        }
                    }

                }
                AddLog("已成功添加到右键菜单。", LogLevel.Success);
            }
            catch (Exception ex)
            {
                AddLog($"添加右键菜单失败: {ex.Message}", LogLevel.Error);
                ChkContextMenu.IsChecked = false;
            }
        }

        private void ChkContextMenu_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isContextMenuInitializing) return;

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(ContextMenuKey, false);
                // Also clean up old invalid keys if they exist
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\ComicArchiver_Pack", false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\ComicArchiver_Extract", false);
                AddLog("已移除右键菜单。", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"移除右键菜单失败: {ex.Message}", LogLevel.Error);
                ChkContextMenu.IsChecked = true;
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
                try
                {
                    using (var p = Process.Start("explorer.exe", path)) { }
                }
                catch (Exception ex)
                {
                    HandyControl.Controls.MessageBox.Show($"打开文件夹失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (File.Exists(path))
            {
                try
                {
                    string parentDir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        using (var p = Process.Start("explorer.exe", $"/select,\"{path}\"")) { }
                    }
                }
                catch (Exception ex)
                {
                    HandyControl.Controls.MessageBox.Show($"定位文件失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                HandyControl.Controls.MessageBox.Show("无法打开，目标不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
                e.Handled = true;
            }
        }

        /// <summary>
        /// 拖拽放置文件/文件夹放置松开鼠标事件响应，提取拖拽路径并设为当前目标。
        /// </summary>
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_isProcessing) return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Handled = true;
                string[] dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (dropped != null && dropped.Length > 0)
                {
                    if (dropped.Length > 1)
                    {
                        AddLog("拖拽无效：不支持同时拖入多个文件或文件夹", LogLevel.Warning);
                        return;
                    }

                    await SetTargetPathAsync(dropped);
                    if (_selectedPaths.Count > 0)
                    {
                        AddLog($"待处理目录: {new DirectoryInfo(_selectedPaths[0]).Name}", LogLevel.Info);
                    }
                }
            }
        }

        /// <summary>
        /// 控制 UI 控件在“运行中”与“空闲中”状态切换时的可操作性（禁用/启用）。
        /// </summary>
        /// <param name="isRunning">当前是否正在运行打包任务</param>
        private void SetUiRunningState(bool isRunning)
        {
            this.AllowDrop = !isRunning;
            // 对于 ProgressButton，禁用会导致变灰覆盖进度条颜色，因此仅屏蔽鼠标交互
            BtnStart.IsHitTestVisible = !isRunning;
            BtnExtract.IsHitTestVisible = !isRunning;
            
            BtnCancel.IsEnabled = isRunning;
            BtnBrowse.IsEnabled = !isRunning;
            TxtTargetDir.IsEnabled = !isRunning;
            RbCbz.IsEnabled = !isRunning;
            RbZip.IsEnabled = !isRunning;
            ChkDeleteOriginal.IsEnabled = !isRunning;
            TxtAdditionalInclude.IsEnabled = !isRunning;

            if (isRunning)
            {
                BtnStart.Progress = 0;
                BtnExtract.Progress = 0;
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
                Foreground = brush,
                Tag = level
            };

            if (CmbLogLevelFilter != null)
            {
                ApplyLogFilter(item);
            }

            LstLogs.Items.Add(item);
            LstLogs.ScrollIntoView(item);
        }

        private void CmbLogLevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstLogs == null) return;
            foreach (ListBoxItem item in LstLogs.Items)
            {
                ApplyLogFilter(item);
            }
        }

        private void ApplyLogFilter(ListBoxItem item)
        {
            if (item.Tag is LogLevel level && CmbLogLevelFilter != null)
            {
                int index = CmbLogLevelFilter.SelectedIndex;
                bool isVisible = true;
                switch (index)
                {
                    case 1: isVisible = level == LogLevel.Info; break;
                    case 2: isVisible = level == LogLevel.Success; break;
                    case 3: isVisible = level == LogLevel.Warning; break;
                    case 4: isVisible = level == LogLevel.Error; break;
                }
                item.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private void SaveThemeSetting(bool isDark)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ThemeSettingsKey))
                {
                    key.SetValue("IsDarkTheme", isDark ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private bool LoadThemeSetting()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ThemeSettingsKey))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("IsDarkTheme");
                        if (val != null && val is int i)
                        {
                            return i == 1;
                        }
                    }
                }
            }
            catch { }
            return true; // 默认暗色
        }

        private void ApplyTheme(bool isDark)
        {
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
        /// 点击 [ToggleThemeCommand] 按钮事件，切换 HandyControl 的 SkinDefault（明亮）与 SkinDark（暗黑）主题。
        /// </summary>
        private void ToggleThemeCommand_Click(object sender, RoutedEventArgs e)
        {
            if (_isThemeInitializing) return;
            
            bool isDark = ToggleThemeCommand.IsChecked == true;
            ApplyTheme(isDark);
            SaveThemeSetting(isDark);
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cts?.Dispose();
        }
    }
}

