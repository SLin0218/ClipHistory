using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using SlinClip.Models;
using SlinClip.Services;
using Wox.Plugin;

namespace SlinClip;

public class Main : IPlugin, IContextMenu, IDisposable
{
    public static string PluginID => "B81B7F13E7824D988F96C03D1A5B2F99";

    private PluginInitContext? _context;
    private DatabaseService? _dbService;
    private ThumbnailService? _thumbService;
    private ClipboardWatcher? _watcher;

    private readonly List<ClipItem> _memCache = new(capacity: 1000);
    private readonly ReaderWriterLockSlim _cacheLock = new();

    private string _pluginDirectory = string.Empty;
    private string _iconDefault = "Images\\clip.dark.png";
    private string _iconText = "Images\\text.png";
    private string _iconImage = "Images\\image.png";
    private string _logPath = string.Empty;

    private const int MaxHistoryLimit = 500;

    public string Name => "Slin-Clip";
    public string Description => "高性能剪贴板历史管理器（支持文本、图片快速过滤与粘贴）";

    private void Log(string message)
    {
        try
        {
            if (!string.IsNullOrEmpty(_logPath))
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public void Init(PluginInitContext context)
    {
        _context = context;
        _pluginDirectory = context.CurrentPluginMetadata.PluginDirectory;

        _iconDefault = Path.Combine(_pluginDirectory, "Images", "clip.dark.png");
        _iconText = Path.Combine(_pluginDirectory, "Images", "text.png");
        _iconImage = Path.Combine(_pluginDirectory, "Images", "image.png");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDataDir = Path.Combine(appData, "PowerToys", "SlinClip");
        var dbPath = Path.Combine(baseDataDir, "slin_clip.db");
        _logPath = Path.Combine(baseDataDir, "plugin.log");

        Log("Slin-Clip Init started...");

        _dbService = new DatabaseService(dbPath);
        _thumbService = new ThumbnailService(baseDataDir);

        var items = _dbService.LoadRecent(MaxHistoryLimit);
        _cacheLock.EnterWriteLock();
        try
        {
            _memCache.Clear();
            _memCache.AddRange(items);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        Log($"Loaded {items.Count} items from SQLite into memory.");

        _watcher = new ClipboardWatcher(_thumbService);
        _watcher.OnClipCaptured += OnClipCaptured;

        Log("ClipboardWatcher initialized.");
    }

    private void OnClipCaptured(ClipItem rawItem)
    {
        if (_dbService == null) return;

        try
        {
            var persistedItem = _dbService.Upsert(rawItem, MaxHistoryLimit);

            _cacheLock.EnterWriteLock();
            try
            {
                _memCache.RemoveAll(x => x.Hash == persistedItem.Hash);
                _memCache.Insert(0, persistedItem);

                if (_memCache.Count > MaxHistoryLimit + 50)
                {
                    var pinned = _memCache.Where(x => x.IsPinned).ToList();
                    var unpinned = _memCache.Where(x => !x.IsPinned).Take(MaxHistoryLimit).ToList();
                    _memCache.Clear();
                    _memCache.AddRange(pinned);
                    _memCache.AddRange(unpinned);
                    _memCache.Sort((a, b) =>
                    {
                        int p = b.IsPinned.CompareTo(a.IsPinned);
                        return p != 0 ? p : b.UpdatedAt.CompareTo(a.UpdatedAt);
                    });
                }
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }

            Log($"Captured new clip: Type={persistedItem.Type}, ID={persistedItem.Id}, Content={(persistedItem.Content.Length > 20 ? persistedItem.Content[..20] : persistedItem.Content)}");
        }
        catch (Exception ex)
        {
            Log($"Error capturing clip: {ex.Message}");
        }
    }

    public List<Result> Query(Query query)
    {
        var rawSearch = query.Search?.Trim() ?? string.Empty;
        var isDedicatedKeyword = !string.IsNullOrEmpty(query.ActionKeyword);

        Log($"Query called: RawQuery='{query.RawQuery}', ActionKeyword='{query.ActionKeyword}', Search='{query.Search}'");

        var tokens = rawSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(t => t.ToLowerInvariant())
                              .ToArray();

        var matchedItems = new List<ClipItem>(capacity: 30);

        _cacheLock.EnterReadLock();
        try
        {
            if (tokens.Length == 0)
            {
                // 无特定搜索词时：
                // 如果用户输入了 cb 专属触发词，则显示最近 30 条；
                // 如果是在全局搜索且没有搜索词，则不干扰全局搜索
                if (isDedicatedKeyword || query.RawQuery.Trim().Equals("cb", StringComparison.OrdinalIgnoreCase))
                {
                    matchedItems.AddRange(_memCache.Take(30));
                }
            }
            else
            {
                foreach (var item in _memCache)
                {
                    bool match = true;
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        if (!item.SearchText.Contains(tokens[i]))
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        matchedItems.Add(item);
                        if (matchedItems.Count >= 40) break;
                    }
                }
            }
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        if (matchedItems.Count == 0)
        {
            // 如果是专属触发词 cb，则提示空或未找到
            if (isDedicatedKeyword || query.RawQuery.Trim().Equals("cb", StringComparison.OrdinalIgnoreCase))
            {
                return new List<Result>
                {
                    new()
                    {
                        Title = string.IsNullOrEmpty(rawSearch) ? "剪贴板历史为空" : "未找到匹配的剪贴板历史",
                        SubTitle = "复制文本或图片后将自动记录在这里",
                        IcoPath = _iconDefault,
                        Score = 100,
                        Action = _ => true
                    }
                };
            }
            return new List<Result>();
        }

        var results = new List<Result>(matchedItems.Count);
        int baseScore = isDedicatedKeyword ? 100 : 50;

        foreach (var item in matchedItems)
        {
            var title = item.Type == ClipType.Image
                ? $"[图片] {item.ImageWidth} x {item.ImageHeight}"
                : (item.Content.Length > 120 ? item.Content.Substring(0, 120).Replace("\r", " ").Replace("\n", " ") + "..." : item.Content.Replace("\r", " ").Replace("\n", " "));

            var icoPath = item.Type == ClipType.Image && !string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath)
                ? item.ThumbnailPath
                : (item.Type == ClipType.Image ? _iconImage : _iconText);

            results.Add(new Result
            {
                Title = title,
                SubTitle = item.GetSubtitle(),
                IcoPath = icoPath,
                Score = baseScore,
                ContextData = item,
                Action = _ =>
                {
                    KeyboardSimulator.SetToClipboard(item, _watcher);
                    KeyboardSimulator.SendCtrlV();
                    return true;
                }
            });
        }

        return results;
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not ClipItem item)
        {
            return new List<ContextMenuResult>();
        }

        var menus = new List<ContextMenuResult>
        {
            new()
            {
                PluginDirectory = _pluginDirectory,
                Title = "仅复制到剪贴板 (Enter直接粘贴)",
                Glyph = "\xE8C8",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ =>
                {
                    KeyboardSimulator.SetToClipboard(item, _watcher);
                    return true;
                }
            },
            new()
            {
                PluginDirectory = _pluginDirectory,
                Title = item.IsPinned ? "取消置顶" : "置顶本条",
                Glyph = "\xE718",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ =>
                {
                    item.IsPinned = !item.IsPinned;
                    _dbService?.TogglePin(item.Id, item.IsPinned);

                    _cacheLock.EnterWriteLock();
                    try
                    {
                        _memCache.Sort((a, b) =>
                        {
                            int p = b.IsPinned.CompareTo(a.IsPinned);
                            return p != 0 ? p : b.UpdatedAt.CompareTo(a.UpdatedAt);
                        });
                    }
                    finally
                    {
                        _cacheLock.ExitWriteLock();
                    }

                    _context?.API.ReQuery();
                    return false;
                }
            },
            new()
            {
                PluginDirectory = _pluginDirectory,
                Title = "从历史记录中删除",
                Glyph = "\xE74D",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ =>
                {
                    _dbService?.Delete(item.Id);

                    _cacheLock.EnterWriteLock();
                    try
                    {
                        _memCache.RemoveAll(x => x.Id == item.Id);
                    }
                    finally
                    {
                        _cacheLock.ExitWriteLock();
                    }

                    _context?.API.ReQuery();
                    return false;
                }
            },
            new()
            {
                PluginDirectory = _pluginDirectory,
                Title = "清空所有非置顶历史",
                Glyph = "\xE75C",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ =>
                {
                    _dbService?.ClearAll(preservePinned: true);

                    _cacheLock.EnterWriteLock();
                    try
                    {
                        _memCache.RemoveAll(x => !x.IsPinned);
                    }
                    finally
                    {
                        _cacheLock.ExitWriteLock();
                    }

                    _context?.API.ReQuery();
                    return false;
                }
            }
        };

        if (item.Type == ClipType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
        {
            menus.Add(new ContextMenuResult
            {
                PluginDirectory = _pluginDirectory,
                Title = "在资源管理器中定位原图",
                Glyph = "\xEC50",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ =>
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.ImagePath}\"");
                    return true;
                }
            });
        }

        return menus;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _dbService?.Dispose();
        _cacheLock?.Dispose();
    }
}
