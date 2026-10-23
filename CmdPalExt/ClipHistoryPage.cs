using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Windows.Foundation;
using Windows.System;
using Microsoft.CommandPalette.Extensions;
using SlinClip.Models;
using SlinClip.Services;

namespace SlinClipCmdPal
{
    public sealed class ClipHistoryPage : IDynamicListPage, IDisposable
    {
        public string Id => "ClipHistoryPage";
        public string Name => "ClipHistory";
        public string Title => I18n.T("剪切板历史 (ClipHistory)", "Clipboard History (ClipHistory)");
        public IIconInfo? Icon => IconInfo.FromAsset("icon.svg");
        public OptionalColor AccentColor => default;
        public bool IsLoading { get; private set; } = false;
        public bool ShowDetails => true;
        public string PlaceholderText => I18n.T("搜索剪切板历史 (文本/图片)...", "Search clipboard history (text/images)...");
        public IFilters? Filters => null;
        public IGridProperties? GridProperties => null;
        public bool HasMoreItems => false;

        public ICommandItem? EmptyContent
        {
            get
            {
                var query = _searchText.Trim();
                return new CommandItem
                {
                    Title = string.IsNullOrWhiteSpace(query)
                        ? I18n.T("剪切板历史为空", "Clipboard history is empty")
                        : string.Format(I18n.T("未找到与 \"{0}\" 匹配的记录", "No records matching \"{0}\""), query),
                    Subtitle = string.IsNullOrWhiteSpace(query)
                        ? I18n.T("复制任意文本或图片即可自动记录到历史", "Copy any text or image to record it in history")
                        : I18n.T("请尝试更换关键词搜索或清空过滤词", "Try searching with different keywords or clear the filter"),
                    Icon = new IconInfo("\uE9CE")
                };
            }
        }

        private readonly DatabaseService _db;
        private readonly ThumbnailService _thumb;
        private readonly ClipboardWatcher _watcher;
        private readonly SettingsService _settingsService;
        public ClipSettingsContentPage? SettingsPage { get; set; }
        private readonly List<ClipItem> _cache = new();
        private readonly object _lock = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value ?? string.Empty;
                    ExtLog.Info($"ClipHistoryPage SearchText changed: {_searchText}");
                    PropChanged?.Invoke(this, new PropChangedEventArgs(nameof(EmptyContent)));
                    ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                }
            }
        }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
        public event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged;

        public ClipHistoryPage(
            DatabaseService db,
            ThumbnailService thumb,
            ClipboardWatcher watcher,
            SettingsService settingsService)
        {
            _db = db;
            _thumb = thumb;
            _watcher = watcher;
            _settingsService = settingsService;

            var limit = _settingsService.Settings.MaxHistoryCount;
            lock (_lock)
            {
                _cache.AddRange(_db.LoadRecent(limit));
            }
            ExtLog.Info($"ClipHistoryPage loaded {_cache.Count} cached items (limit={limit}).");

            _watcher.OnClipCaptured += OnClipCaptured;
            _settingsService.OnSettingsChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            lock (_lock)
            {
                var maxLimit = settings.MaxHistoryCount;
                while (_cache.Count > maxLimit)
                {
                    var lastUnpinned = _cache.FindLastIndex(c => !c.IsPinned);
                    if (lastUnpinned >= 0) _cache.RemoveAt(lastUnpinned);
                    else break;
                }
            }
            ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
        }

        private void OnClipCaptured(ClipItem item)
        {
            var maxLimit = _settingsService.Settings.MaxHistoryCount;
            ExtLog.Info($"OnClipCaptured: Type={item.Type}, ContentPreview={item.Content.Substring(0, Math.Min(item.Content.Length, 30))}");
            lock (_lock)
            {
                _db.Upsert(item, maxLimit);

                var existingIndex = _cache.FindIndex(c => c.Hash == item.Hash);
                if (existingIndex >= 0)
                {
                    var existing = _cache[existingIndex];
                    existing.UpdatedAt = DateTime.Now;
                    if (!existing.IsPinned)
                    {
                        _cache.RemoveAt(existingIndex);
                        var firstUnpinned = _cache.FindIndex(c => !c.IsPinned);
                        if (firstUnpinned >= 0) _cache.Insert(firstUnpinned, existing);
                        else _cache.Add(existing);
                    }
                }
                else
                {
                    var firstUnpinned = _cache.FindIndex(c => !c.IsPinned);
                    if (firstUnpinned >= 0) _cache.Insert(firstUnpinned, item);
                    else _cache.Insert(0, item);
                }

                if (_cache.Count > maxLimit)
                {
                    var lastUnpinned = _cache.FindLastIndex(c => !c.IsPinned);
                    if (lastUnpinned >= 0) _cache.RemoveAt(lastUnpinned);
                }
            }

            ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
        }

        public void ClearHistory(bool preservePinned = true)
        {
            ExtLog.Info($"ClearHistory called (preservePinned={preservePinned})");
            _db.ClearAll(preservePinned);
            lock (_lock)
            {
                if (preservePinned)
                {
                    _cache.RemoveAll(c => !c.IsPinned);
                }
                else
                {
                    _cache.Clear();
                }
            }
            ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
        }

        public void ClearSystemClipboard()
        {
            ExtLog.Info("ClearSystemClipboard called");
            KeyboardSimulator.ClearClipboard(_watcher);
        }

        public int UnpinnedCount
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count(c => !c.IsPinned);
                }
            }
        }

        public int TotalCount
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }

        public void LoadMore() { }

        public IListItem[] GetItems()
        {
            ExtLog.Info($"ClipHistoryPage.GetItems called (SearchText='{_searchText}')");
            List<ClipItem> filtered;
            lock (_lock)
            {
                var q = _searchText.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(q))
                {
                    filtered = _cache.Take(80).ToList();
                }
                else
                {
                    filtered = _cache.Where(c =>
                    {
                        if (c.Type == ClipType.Text)
                        {
                            return c.SearchText.Contains(q);
                        }
                        else
                        {
                            return q == "img" || q == "pic" || q == "image" || q == "图片" ||
                                   c.SearchText.Contains(q) || c.Content.ToLowerInvariant().Contains(q);
                        }
                    }).Take(80).ToList();
                }
            }

            var list = new List<IListItem>(filtered.Count + 2);
            var qClean = _searchText.Trim().ToLowerInvariant();

            // 当搜索包含设置指令时，提供直达官方设置页的快捷通道
            if (qClean.Contains("set") || qClean.Contains("设置") || qClean.Contains("config") || qClean.Contains("配置"))
            {
                var actionSettings = new ListItem
                {
                    Section = I18n.T("快捷设置", "Quick Settings"),
                    Title = I18n.T("打开 ClipHistory 扩展设置", "Open ClipHistory Settings"),
                    Subtitle = I18n.T("配置最大保存条目数与单条内容大小限制", "Configure max history count and content size limit"),
                    Icon = new IconInfo("\uE713"),
                    Command = new ActionCommand(I18n.T("打开设置页面", "Open Settings Page"), () => CommandResult.GoToPage("ClipHistorySettingsPage"))
                };
                list.Add(actionSettings);
            }

            // 当搜索包含清空/清除指令时，在最上方插入快捷清除条目
            if (qClean.Contains("clear") || qClean.Contains("清空") || qClean.Contains("清除") || qClean.Contains("clean"))
            {
                var actionClearUnpinned = new ListItem
                {
                    Section = I18n.T("快捷清除操作", "Quick Clear Actions"),
                    Title = I18n.T("清空常规剪切板历史 (保留置顶项)", "Clear Unpinned History (Keep Pinned)"),
                    Subtitle = string.Format(I18n.T("立即清空常规历史记录（当前共 {0} 条常规历史）并清空系统当前剪切板", "Clear unpinned history ({0} items) and clear the system clipboard"), UnpinnedCount),
                    Icon = new IconInfo("\uE74D"),
                    Command = new ActionCommand(I18n.T("立即清理未置顶历史与剪贴板", "Clear Unpinned History & Clipboard"), () =>
                    {
                        ClearHistory(preservePinned: true);
                        ClearSystemClipboard();
                        return CommandResult.KeepOpen();
                    })
                };
                list.Add(actionClearUnpinned);

                var actionClearSys = new ListItem
                {
                    Section = I18n.T("快捷清除操作", "Quick Clear Actions"),
                    Title = I18n.T("仅清空当前系统剪切板内容", "Clear System Clipboard Only"),
                    Subtitle = I18n.T("立即清空 Windows 内存中当前的剪贴板数据（历史记录完整保留）", "Clear current Windows clipboard data immediately (history is preserved)"),
                    Icon = new IconInfo("\uE75C"),
                    Command = new ActionCommand(I18n.T("立即清空系统剪贴板", "Clear System Clipboard"), () =>
                    {
                        ClearSystemClipboard();
                        return CommandResult.KeepOpen();
                    })
                };
                list.Add(actionClearSys);
            }

            foreach (var item in filtered)
            {
                list.Add(CreateListItem(item));
            }

            // 当没有任何匹配项时返回空数组，触发 CmdPal 显示 EmptyContent 原生占位图文
            // 避免产生无 Section 的 Dummy Item，彻底消除顶部的水平分割线
            if (list.Count == 0)
            {
                return Array.Empty<IListItem>();
            }

            return list.ToArray();
        }

        public void Dispose()
        {
            _watcher.OnClipCaptured -= OnClipCaptured;
            _settingsService.OnSettingsChanged -= OnSettingsChanged;
        }

        private static string FormatTextForPreview(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return I18n.T("*(空内容)*", "*(Empty)*");

            var normalized = rawText.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalized.Split('\n');
            var sb = new System.Text.StringBuilder();

            const int maxPreviewLines = 80;
            int totalLines = lines.Length;
            int renderLines = Math.Min(totalLines, maxPreviewLines);

            for (int i = 0; i < renderLines; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                {
                    sb.Append("&nbsp;  \n");
                    continue;
                }

                // 统计前导空格与制表符并转换为 &nbsp;，既保留缩进，又防止 Markdown 触发 4 空格缩进代码块（产生嵌套卡片背景）
                int spaceCount = 0;
                int idx = 0;
                while (idx < line.Length)
                {
                    if (line[idx] == ' ') { spaceCount++; idx++; }
                    else if (line[idx] == '\t') { spaceCount += 4; idx++; }
                    else break;
                }

                string rest = line.Substring(idx);
                // HTML 转义防止诸如 <UserRequest> 或 <div /> 被误当作 HTML 标签丢弃
                string encoded = System.Net.WebUtility.HtmlEncode(rest);

                if (spaceCount > 0)
                {
                    for (int s = 0; s < spaceCount; s++)
                    {
                        sb.Append("&nbsp;");
                    }
                }

                sb.Append(encoded);
                sb.Append("  \n"); // 结尾两个空格强制 Markdown 换行
            }

            if (totalLines > maxPreviewLines)
            {
                sb.Append($"\n\n*... ({string.Format(I18n.T("已截断预览，后余 {0} 行", "truncated, {0} more lines"), totalLines - maxPreviewLines)})*  \n");
            }

            return sb.ToString();
        }

        private static string MakeBreakable(string text, int maxUnbrokenLength = 20)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder();
            int currentLength = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                sb.Append(c);
                currentLength++;
                if (c == '\\' || c == '/' || c == '_' || c == '-' || currentLength >= maxUnbrokenLength)
                {
                    sb.Append('\u200B'); // 插入零宽字符，提供安全的自适应折行断点
                    currentLength = 0;
                }
            }
            return sb.ToString();
        }

        private ListItem CreateListItem(ClipItem item)
        {
            var li = new ListItem
            {
                Section = item.IsPinned ? I18n.T("置顶记录", "Pinned") : I18n.T("最近历史", "Recent History")
            };

            if (item.IsPinned)
            {
                li.Tags = new ITag[] { new Tag(I18n.T("已置顶", "Pinned"), I18n.T("此项已固定在顶部", "This item is pinned to the top"), new IconInfo("\uE718")) };
            }

            if (item.Type == ClipType.Text)
            {
                var preview = item.Content.Trim().Replace("\r\n", " ").Replace("\n", " ");
                if (preview.Length > 80) preview = preview.Substring(0, 80) + "...";
                li.Title = preview;
                li.Subtitle = string.Empty; // 列表区域不显示字数/行数等元数据
                li.Icon = new IconInfo("\uE8C8");

                var lineCount = item.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
                var formattedBody = FormatTextForPreview(item.Content);

                li.Details = new Details
                {
                    Title = "",
                    HeroImage = null,
                    Body = $"{formattedBody}\n\n" +
                           $"---\n\n" +
                           $"{I18n.T("时间", "Time")}: {item.CreatedAt:yyyy-MM-dd HH:mm:ss}\n\n" +
                           $"{I18n.T("长度", "Length")}: {item.CharCount} {I18n.T("字符", "characters")}\n\n" +
                           $"{I18n.T("行数", "Lines")}: {lineCount}"
                };
            }
            else
            {
                // 列表左侧图标固定为图片图标
                li.Icon = new IconInfo("\uEB9F");

                // 列表区域标题与副标题：简洁干净，不在后面显示分辨率/文件大小等元数据
                li.Title = I18n.T("图片", "Image");
                li.Subtitle = string.Empty;

                // 详情预览区域图片路径与大小
                var previewImgPath = !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath)
                    ? item.ImagePath
                    : item.ThumbnailPath;

                string sizeStr = "";
                if (!string.IsNullOrEmpty(previewImgPath) && File.Exists(previewImgPath))
                {
                    try
                    {
                        var fi = new FileInfo(previewImgPath);
                        if (fi.Exists && fi.Length > 0)
                        {
                            sizeStr = fi.Length < 1024 * 1024
                                ? $"{fi.Length / 1024.0:0.#} KB"
                                : $"{fi.Length / (1024.0 * 1024):0.##} MB";
                        }
                    }
                    catch { }
                }

                // 详情预览区域：使用适屏缩放图（最大 440x340），防止原图过大导致显示不全被截断
                string imageMarkdown = "";
                if (!string.IsNullOrEmpty(previewImgPath) && File.Exists(previewImgPath))
                {
                    var displayImgPath = _thumb.GetOrCreatePreviewImage(previewImgPath);
                    var fileUri = new Uri(displayImgPath).AbsoluteUri;
                    imageMarkdown = $"![{I18n.T("图片预览", "Image Preview")}]({fileUri})\n\n";
                }
                else
                {
                    imageMarkdown = I18n.T("_图片文件不存在或已移动_\n\n", "_Image file not found or moved_\n\n");
                }

                var sizeDisplay = !string.IsNullOrEmpty(sizeStr) ? sizeStr : $"{item.ImageWidth} × {item.ImageHeight}";
                var locationDisplay = !string.IsNullOrEmpty(previewImgPath)
                    ? MakeBreakable(previewImgPath)
                    : I18n.T("未知", "Unknown");

                li.Details = new Details
                {
                    Title = "",
                    HeroImage = null,
                    Body = $"{imageMarkdown}" +
                           $"---\n\n" +
                           $"{I18n.T("时间", "Time")}: {item.CreatedAt:yyyy-MM-dd HH:mm:ss}\n\n" +
                           $"{I18n.T("大小", "Size")}: {sizeDisplay}\n\n" +
                           $"{I18n.T("位置", "Location")}: {locationDisplay}"
                };
            }

            // 默认回车主动作：复制（CmdPal 会自动作为菜单首项展示）
            li.Command = new ActionCommand(I18n.T("复制", "Copy"), () =>
            {
                ExtLog.Info($"Default copy item {item.Id}");
                KeyboardSimulator.SetToClipboard(item, _watcher);
                return CommandResult.Dismiss();
            }, new IconInfo("\uE8C8"));

            var more = new List<IContextItem>();

            // 1. 固定(Ctrl+Enter) / 取消固定(Ctrl+Enter)
            more.Add(new CommandContextItem(item.IsPinned ? I18n.T("取消固定", "Unpin") : I18n.T("固定", "Pin"), () =>
            {
                item.IsPinned = !item.IsPinned;
                _db.TogglePin(item.Id, item.IsPinned);
                lock (_lock)
                {
                    _cache.Sort((a, b) =>
                    {
                        int pinComp = b.IsPinned.CompareTo(a.IsPinned);
                        if (pinComp != 0) return pinComp;
                        return b.UpdatedAt.CompareTo(a.UpdatedAt);
                    });
                }
                ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                return CommandResult.KeepOpen();
            }, new IconInfo("\uE718"))
            {
                RequestedShortcut = new KeyChord(VirtualKeyModifiers.Control, (int)VirtualKey.Enter, 0)
            });

            // 图片项附带在资源管理器中定位
            if (item.Type == ClipType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                var imgPath = item.ImagePath;
                more.Add(new CommandContextItem(I18n.T("在资源管理器中定位图片", "Locate in File Explorer"), () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{imgPath}\"");
                    }
                    catch { }
                    return CommandResult.Dismiss();
                }, new IconInfo("\uEC50")));
            }

            // 2. 删除(Ctrl+Backspace) - 二次确认并清空系统剪贴板
            more.Add(new CommandContextItem(I18n.T("删除", "Delete"), () =>
            {
                return CommandResult.Confirm(new ConfirmationArgs(
                    I18n.T("确认删除记录", "Confirm Delete"),
                    I18n.T("确定要从历史记录中删除该条剪切板内容吗？此操作不可恢复。", "Are you sure you want to delete this item from history? This action cannot be undone."),
                    new ActionCommand(I18n.T("确定删除", "Delete"), () =>
                    {
                        ExtLog.Info($"Confirmed delete item {item.Id}");
                        _thumb.DeleteImageFiles(item.ImagePath, item.ThumbnailPath);
                        _db.Delete(item.Id);
                        lock (_lock)
                        {
                            _cache.RemoveAll(c => c.Id == item.Id);
                        }
                        ClearSystemClipboard();
                        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                        return CommandResult.KeepOpen();
                    }, new IconInfo("\uE74D")),
                    isCritical: true
                ));
            }, new IconInfo("\uE74D"), isCritical: true)
            {
                RequestedShortcut = new KeyChord(VirtualKeyModifiers.Control, (int)VirtualKey.Back, 0)
            });

            // 3. 清空所有 - 二次确认
            more.Add(new CommandContextItem(I18n.T("清空所有", "Clear All"), () =>
            {
                return CommandResult.Confirm(new ConfirmationArgs(
                    I18n.T("确认清空全部历史", "Confirm Clear All"),
                    I18n.T("确定要清空全部剪切板历史（包括已固定的置顶记录）及当前系统剪贴板吗？此操作不可恢复。", "Are you sure you want to clear all clipboard history (including pinned items) and the system clipboard? This action cannot be undone."),
                    new ActionCommand(I18n.T("确定清空全部", "Clear All"), () =>
                    {
                        ExtLog.Info("Confirmed clear all history and clipboard");
                        _db.ClearAll(preservePinned: false);
                        lock (_lock)
                        {
                            _cache.Clear();
                        }
                        ClearSystemClipboard();
                        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                        return CommandResult.KeepOpen();
                    }, new IconInfo("\uE74D")),
                    isCritical: true
                ));
            }, new IconInfo("\uE74D"), isCritical: true));

            // 4. 清空未固定 - 二次确认
            more.Add(new CommandContextItem(I18n.T("清空未固定", "Clear Unpinned"), () =>
            {
                return CommandResult.Confirm(new ConfirmationArgs(
                    I18n.T("确认清空常规历史", "Confirm Clear Unpinned"),
                    I18n.T("确定要清空所有常规历史记录吗？已固定的置顶记录将被完整保留。", "Are you sure you want to clear all unpinned history? Pinned items will be preserved."),
                    new ActionCommand(I18n.T("确定清空未固定", "Clear Unpinned"), () =>
                    {
                        ExtLog.Info("Confirmed clear unpinned history");
                        _db.ClearAll(preservePinned: true);
                        lock (_lock)
                        {
                            _cache.RemoveAll(c => !c.IsPinned);
                        }
                        ClearSystemClipboard();
                        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                        return CommandResult.KeepOpen();
                    }, new IconInfo("\uE72C")),
                    isCritical: true
                ));
            }, new IconInfo("\uE72C"), isCritical: true));

            li.MoreCommands = more.ToArray();
            return li;
        }
    }
}
