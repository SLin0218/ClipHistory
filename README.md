# ClipHistory (PowerToys Command Palette 剪贴板历史扩展)

[English](#english) | [简体中文](#简体中文)

---

<a name="简体中文"></a>
## 🇨🇳 简体中文

高性能、低资源占用的 Windows PowerToys Command Palette 原生剪贴板历史扩展。全面支持**文本**与**图片**历史捕获、毫秒级内存模糊搜索。

### 🚀 呼出与使用方式

在 Windows 中按 **`Alt + Space`** 唤起 PowerToys Command Palette：

1. **进入插件**：
   - 输入别名：**`ch`** 或 **`clip`**，直接进入 ClipHistory 搜索列表。
   - 或输入 **`剪贴板`** / **`历史`** 均可快速进入。
2. **快速检索**：
   - 键入任意关键词即时过滤文本。
   - 键入 `img`、`pic` 或 `图片` 快速定位所有图像条目。
3. **管理与配置**：
   - 点击顶部或菜单中的 **设置 (Settings)**，进入配置页调整保留数量与语言。

---

### 🛠️ 项目结构

```text
ClipHistory/
├── CmdPalExt/                  # PowerToys Command Palette 扩展工程
│   ├── ClipHistoryCmdPal.csproj# 目标 net8.0-windows10.0.22000.0 (WinRT COM Server)
│   ├── ClipExtension.cs        # IExtension 根入口与生命周期管理
│   ├── ClipCommandProvider.cs  # TopLevelCommands 命令提供者
│   ├── ClipHistoryPage.cs      # 图文列表搜索页、Markdown 详情预览与快捷键
│   ├── ClipSettingsContentPage.cs # 扩展设置表单页面
│   ├── Package.appxmanifest    # AppX 打包清单
│   ├── Assets/                 # 应用与扩展图标资源
│   └── Program.cs              # COM 服务注册入口 (-RegisterProcessAsComServer)
├── Models/
│   ├── ClipItem.cs             # 核心数据条目模型
│   └── ClipType.cs             # 剪贴板类型枚举 (Text / Image)
├── Services/
│   ├── ClipboardWatcher.cs     # Win32 剪贴板底层监听 (WM_CLIPBOARDUPDATE)
│   ├── DatabaseService.cs      # SQLite WAL 读写与自动修剪引擎
│   ├── ThumbnailService.cs     # 缩略图生成与内存安全文件存储
│   ├── SettingsService.cs      # 用户配置持久化服务
│   └── I18n.cs                 # 中英双语国际化字典
├── distribution/               # 微软官方扩展商店与 WinGet 提交清单
└── build.ps1                   # 一键编译、打包、测试与部署脚本
```

---

### 📦 本地编译与部署

通过根目录下的 `build.ps1` 脚本进行一键化构建与注册：

```powershell
# 一键编译并部署 CmdPal 扩展并自动重启生效：
.\build.ps1 -Deploy -Restart
```

**数据存储路径**：
* 数据库：`%APPDATA%\PowerToys\ClipHistory\clip_history.db`
* 缓存图片目录：`%APPDATA%\PowerToys\ClipHistory\Images\`
* 运行日志：`%APPDATA%\PowerToys\ClipHistory\cmdpal_extension.log`

---

<a name="english"></a>
## 🌐 English

High-performance, memory-efficient Windows PowerToys Command Palette native clipboard history extension. Features text and image history capture, and sub-millisecond in-memory fuzzy search.

### 🚀 Usage

Press **`Alt + Space`** to open PowerToys Command Palette:

1. **Open Extension**:
   - Type alias: **`ch`** or **`clip`** to jump directly into the ClipHistory search page.
   - Or search **`clipboard`** / **`history`**.
2. **Instant Search**:
   - Type any keyword to instantly filter text entries.
   - Type `img`, `pic`, or `image` to quickly filter image entries.
3. **Settings & Configuration**:
   - Click **Settings** in the top bar or menu to customize max history limits and language.

---

### 🛠️ Project Structure

```text
ClipHistory /
├── CmdPalExt/                  # PowerToys Command Palette extension project
│   ├── ClipHistoryCmdPal.csproj# Targets net8.0-windows10.0.22000.0 (WinRT COM Server)
│   ├── ClipExtension.cs        # IExtension root entry and lifecycle management
│   ├── ClipCommandProvider.cs  # TopLevelCommands provider
│   ├── ClipHistoryPage.cs      # Dynamic list search page, Markdown preview, and shortcuts
│   ├── ClipSettingsContentPage.cs # Extension settings AdaptiveCard form page
│   ├── Package.appxmanifest    # AppX package manifest
│   ├── Assets/                 # Application & extension icons
│   └── Program.cs              # COM server entry (-RegisterProcessAsComServer)
├── Models/
│   ├── ClipItem.cs             # Core clipboard item model
│   └── ClipType.cs             # Item type enum (Text / Image)
├── Services/
│   ├── ClipboardWatcher.cs     # Win32 clipboard listener (WM_CLIPBOARDUPDATE)
│   ├── DatabaseService.cs      # SQLite WAL persistence and LRU prune engine
│   ├── ThumbnailService.cs     # Thumbnail generation and leak-free image storage
│   ├── SettingsService.cs      # User settings persistence service
│   └── I18n.cs                 # Bilingual (zh-CN & en-US) localization dictionary
├── distribution/               # CmdPal extension gallery & WinGet manifests
└── build.ps1                   # One-click build, package, test, and deploy script
```

---

### 📦 Local Build & Deployment

Use the [`build.ps1`](build.ps1) PowerShell script to build, package, and deploy with one click:

```powershell
# Build and deploy extension, and restart PowerToys:
.\build.ps1 -Deploy -Restart
```

**Data & Log Paths**:
* SQLite Database: `%APPDATA%\PowerToys\ClipHistory\clip_history.db`
* Cached Images: `%APPDATA%\PowerToys\ClipHistory\Images\`
* CmdPal Extension Log: `%APPDATA%\PowerToys\ClipHistory\cmdpal_extension.log`
