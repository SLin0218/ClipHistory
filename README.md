# Slin-Clip (PowerToys Run / CmdPal 剪贴板历史插件)

高性能、低延迟的 Windows PowerToys 剪贴板管理插件。原生支持 **PowerToys Run V2 (Command Palette / CmdPal)** 与 **PowerToys Run V1**，支持**文本**与**图片**历史记录，具备毫秒级内存模糊过滤与 SQLite WAL 高性能持久化。

---

## ✨ 核心特性

1. **多模态剪贴板支持**：
   - **文本**：自动去重、计算字符数与行数、保留完整格式。
   - **图片**：自动捕获并异步保存为轻量 PNG，生成 96px 缩略图用于列表秒级渲染，避免原图暴占内存。
2. **极速过滤与性能平衡**：
   - **两层架构（Two-tier Cache）**：启动时将最近历史条目元数据载入内存，用户在命令面板键入时**100% 纯内存过滤**（耗时通常 < 1ms），杜绝磁盘 I/O 引起的按键卡顿。
   - **SQLite WAL 模式**：后台异步持久化，开启 Write-Ahead Logging 与 NORMAL 同步，兼顾极高写入吞吐与断电安全性。
   - **LRU 自动淘汰**：默认保存 500 条记录（可配置），超出上限自动清理未置顶的旧记录，并同步删除过期的物理图片文件。
3. **无缝自动粘贴**：
   - 回车选定历史后，自动写回剪贴板并模拟 `Ctrl + V` 粘贴到目标窗口。
   - 内置防自循环机制，避免自身写回剪贴板时被重复录入。
4. **丰富的交互与右键菜单**：
   - 📌 **置顶**：收藏重要文本/图片，置顶项永不被 LRU 淘汰。
   - 📋 **仅复制**：只写入剪切板，不自动触发 Ctrl+V。
   - 🗑️ **删除单条 / 清空未置顶历史**。
   - 🖼️ **图片预览卡片**：右侧详情窗格直接显示图片高分预览与详细元数据。

---

## 🚀 使用方法

### 呼出插件（PowerToys Run V2 / Command Palette）

按全局快捷键 **`Alt + Space`** 唤起 Command Palette：

1. **直接搜索**：键入触发词或别名：
   - 输入 **`sc`** 或 **`clip`**：直接进入 Slin-Clip 剪切板管理页面。
   - 或输入 **`剪切板`** / **`Slin`**：在全局命令列表中点击进入。
   - 如果启用了 Windows 内置剪贴板扩展，亦可输入 **`cb`**。
2. **在列表中过滤**：
   - 输入任意关键词即时过滤文本。
   - 输入 `img` / `pic` / `图片` 快速列出所有捕获的图像。
3. **快捷键操作**：
   - **Enter / 单击**：复制并自动模拟 `Ctrl + V` 粘贴到前台窗口。
   - **Tab / 右键 / ... 菜单**：打开功能菜单（仅复制、置顶/取消置顶、删除记录、清空历史）。

---

## 🛠️ 项目目录结构

```text
Slin-Clip/
├── CmdPalExt/                # PowerToys Run V2 (Command Palette) 扩展工程
│   ├── SlinClipCmdPal.csproj # 目标 net8.0-windows10.0.22000.0，引用 CsWinRT 与 WinRTServer
│   ├── ClipExtension.cs      # IExtension 根入口与 COM CLSID 绑定
│   ├── ClipCommandProvider.cs# ICommandProvider 命令提供者
│   ├── ClipHistoryPage.cs    # IDynamicListPage 动态图文搜索页面与右侧详情卡片
│   ├── CmdPalModels.cs       # WinRT 接口投影模型与日志工具
│   ├── Program.cs            # -RegisterProcessAsComServer COM 服务激活
│   ├── Assets/               # AppX 应用图标与资产
│   └── publish/              # 编译发布产物与 AppxManifest.xml
├── SlinClip.csproj           # PowerToys Run V1 (.NET 8 WPF 类库)
├── plugin.json               # PowerToys Run V1 插件元数据
├── Main.cs                   # PowerToys Run V1 (Wox.Plugin) 插件实现
├── Models/
│   ├── ClipItem.cs           # 剪贴板条目通用数据模型
│   └── ClipType.cs           # 条目类型 (Text / Image)
├── Services/
│   ├── ClipboardWatcher.cs   # Win32 AddClipboardFormatListener 后台监听器
│   ├── DatabaseService.cs    # SQLite WAL 存储引擎与 LRU 自动淘汰
│   ├── ThumbnailService.cs   # 图片缩略图生成与文件持久化
│   └── KeyboardSimulator.cs  # Win32 SendInput 自动粘贴与键盘模拟
├── Images/                   # 图标资源
└── build.ps1                 # 一键编译、打包、注册与部署脚本
```

---

## 📦 编译与部署

通过根目录下的 `build.ps1` 脚本即可一键完成全流程：

```powershell
# 1. 一键编译并部署 PowerToys Run V2 (CmdPal) 扩展并自动重启 CmdPal：
.\build.ps1 -Target CmdPal -Deploy -Restart

# 2. 一键编译并部署 PowerToys Run V1 插件：
.\build.ps1 -Target V1 -Deploy -Restart

# 3. 编译两者：
.\build.ps1 -Target All -Deploy -Restart
```

数据与日志存储位置：
* **SQLite 数据库**：`%APPDATA%\PowerToys\SlinClip\slin_clip.db`
* **缓存图片目录**：`%APPDATA%\PowerToys\SlinClip\Images\`
* **CmdPal 运行日志**：`%APPDATA%\PowerToys\SlinClip\cmdpal_extension.log`
