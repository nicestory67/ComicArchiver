# ComicArchiver 漫画章节批量打包工具

<p align="center">
  <img src="icons8_moleskine.ico" width="80" height="80" alt="ComicArchiver Logo" />
</p>

<p align="center">
  <b>一款专为漫画/图集整理设计的轻量级、多线程批量打包与解压工具</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-Framework%204.8-512BD4?logo=dotnet" alt=".NET Framework 4.8" />
  <img src="https://img.shields.io/badge/WPF-HandyControl-brightgreen" alt="HandyControl" />
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License" />
</p>

---

## 📖 简介 (Introduction)

**ComicArchiver** 是一款专为漫画读者和资源整理爱好者打造的 Windows 桌面工具。它能够智能扫描文件夹内的漫画章节目录，自动过滤非漫画资源与系统隐藏文件，将各章节快速批量打包为漫画阅读器标准通用的 **`.cbz`** 或 **`.zip`** 压缩包；同时支持一键批量逆向解压。

内置多线程并发处理、Windows 资源管理器右键快捷菜单、跨进程命名管道 (IPC) 单实例通信，以及暗黑/明亮主题切换，提供丝滑高效的使用体验。

---

## ✨ 核心特性 (Features)

### 📦 智能批量打包
- **智能目录识别**：自动递归扫描目录中的漫画章节文件夹，智能识别主流图片格式（`.jpg`, `.jpeg`, `.png`, `.webp`, `.bmp`, `.gif`）。
- **灵活的打包层级**：若目录下包含多个子章节文件夹，则按章节分别独立打包；若单层目录直接包含图片，则智能回退将该目录作为单本漫画打包。
- **自定义文件包含**：支持自定义通配符规则（默认 `*.xml`），打包时自动保留 `ComicInfo.xml` 等元数据文件。
- **智能系统文件过滤**：自动跳过 `.git`、`.thumbnails`、`@eaDir`、`$RECYCLE.BIN` 以及隐藏/系统属性文件夹。
- **源文件清理选项**：打包成功后可选择自动清理源文件夹，支持解除只读属性与多次重试机制。

### 📂 批量解压与安全防护
- **一键解压**：批量检索并解压 `.cbz` 与 `.zip` 压缩包到对应的同名独立文件夹。
- **Zip Slip 漏洞防御**：内置严格的路径越界安全校验，杜绝恶意压缩包穿透解压。
- **原包自动清理**：解压成功后可选自动删除原始压缩包。

### ⚡ 高性能多线程
- **并发调度**：内置基于 `Parallel.ForEach` 的多线程异步架构，支持 **1 ~ 32 线程**（默认 10 线程）自由调节。
- **随时取消与回滚**：支持在处理过程中随时取消，取消后自动清理未完成的临时文件。

### 🪟 深度 Windows 集成
- **资源管理器右键菜单**：
  - **文件夹右键**：提供级联菜单项 `智能打包 (自动扫描)` 与 `一键解压 (向下递归)`。
  - **`.cbz` / `.zip` 文件右键**：提供 `使用 ComicArchiver 解压`。
- **单实例与 IPC 联动**：基于 Windows 命名管道（Named Pipe），重复打开或通过右键菜单调用时，自动唤醒前台主窗口并将路径无缝追加至当前实例。
- **拖拽支持**：支持直接将目标文件夹或压缩包拖入窗口。

### 🎨 现代 UI 交互
- **HandyControl 主题**：现代化卡片式布局，支持明亮 / 暗黑（Dark/Light）主题一键切换与持久化存储。
- **实时日志系统**：提供详细的处理日志，支持按级别过滤（全部、信息、成功、警告、错误），直观展示百分比进度。
- **便携单文件**：集成 `Costura.Fody`，编译后输出为无需安装的单一 `.exe` 文件（如 `ComicArchiver v1.0.1.0.exe`）。

---

## 🚀 快速上手 (Quick Start)

### 系统要求
- **操作系统**：Windows 7 SP1 / Windows 10 / Windows 11 (x86/x64)
- **运行环境**：[.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)

### 使用方式

#### 1. 图形界面操作 (GUI)
1. 运行 `ComicArchiver.exe`。
2. 拖入或通过「浏览...」选择需要处理的漫画目录。
3. 选择打包格式（`CBZ` 或 `ZIP`）、线程数以及是否清理原文件。
4. 点击 **「🚀 开始打包」** 或 **「📦 开始解压」**。

#### 2. 右键菜单集成 (Context Menu)
- 勾选主界面左下角的 **「添加到右键菜单」** 即可完成注册。
- 在任意文件夹或 `.cbz`/`.zip` 文件上点击鼠标右键，直接选择对应的处理命令。

#### 3. 命令行参数 (CLI / Silent Mode)
支持通过命令行直接调用并自动执行：

```powershell
# 自动打包指定目录下的漫画文件夹
ComicArchiver.exe "D:\Manga\OnePiece" /autorun:pack

# 自动解压指定目录下的 CBZ/ZIP 归档
ComicArchiver.exe "D:\Manga\OnePiece" /autorun:extract
```

---

## 🛠️ 项目构建 (Build from Source)

### 依赖项
- [HandyControl](https://github.com/HandyOrg/HandyControl) (v3.5.1) - WPF 控件库
- [Ookii.Dialogs.Wpf](https://github.com/ookii-dialogs/ookii-dialogs-wpf) (v5.0.1) - 文件夹选取对话框
- [Costura.Fody](https://github.com/Fody/Costura) (v6.2.0) - 依赖打包为单文件

### 编译步骤
1. 克隆本仓库到本地：
   ```bash
   git clone https://github.com/nicestory67/ComicArchiver.git
   ```
2. 使用 **Visual Studio 2022** 打开 `ComicArchiver.slnx` 或 `ComicArchiver.csproj`。
3. 或者直接在终端使用 .NET CLI 进行编译：
   ```powershell
   dotnet build ComicArchiver.csproj -c Release
   ```
4. 编译完成后，单文件可执行程序将生成在 `bin/Release/net48/` 目录下（例如 `ComicArchiver v1.0.1.0.exe`）。

---

## ⚙️ 注册表配置说明 (Registry)

本软件相关配置保存在 Windows 当前用户的注册表路径下：
- **软件配置**：`HKCU\Software\ComicArchiver\Settings`
  - `IsDarkTheme`: 主题偏好 (0: 明亮, 1: 暗黑)
  - `ThreadCount`: 记住的并发线程数
- **右键菜单**：
  - `HKCU\Software\Classes\Directory\shell\ComicArchiver`
  - `HKCU\Software\Classes\SystemFileAssociations\.cbz\shell\ComicArchiver_Extract`
  - `HKCU\Software\Classes\SystemFileAssociations\.zip\shell\ComicArchiver_Extract`

---

## 📄 开源许可 (License)

本项目采用 [MIT License](LICENSE) 授权。
