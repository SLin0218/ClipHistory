using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Microsoft.CommandPalette.Extensions;
using SlinClip.Models;
using SlinClip.Services;

namespace SlinClipCmdPal
{
    public sealed class ClipHistoryPage : IDynamicListPage
    {
        public string Id => "SlinClipHistoryPage";
        public string Name => "Slin-Clip History";
        public string Title => "剪切板历史 (Slin-Clip)";
        public IIconInfo? Icon { get; } = new IconInfo("\uE8C8");
        public OptionalColor AccentColor => default;
        public bool IsLoading { get; private set; } = false;
        public bool ShowDetails => true;
        public string PlaceholderText => "搜索剪切板历史 (文本/图片)...";
        public IFilters? Filters => null;
        public IGridProperties? GridProperties => null;
        public bool HasMoreItems => false;
        public ICommandItem? EmptyContent => null;

        private readonly DatabaseService _db;
        private readonly ThumbnailService _thumb;
        private readonly ClipboardWatcher _watcher;
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
                    ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                }
            }
        }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
        public event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged;

        public ClipHistoryPage(DatabaseService db, ThumbnailService thumb, ClipboardWatcher watcher)
        {
            _db = db;
            _thumb = thumb;
            _watcher = watcher;

            lock (_lock)
            {
                _cache.AddRange(_db.LoadRecent(500));
            }
            ExtLog.Info($"ClipHistoryPage loaded {_cache.Count} cached items.");

            _watcher.OnClipCaptured += OnClipCaptured;
        }

        private void OnClipCaptured(ClipItem item)
        {
            ExtLog.Info($"OnClipCaptured: Type={item.Type}, ContentPreview={item.Content.Substring(0, Math.Min(item.Content.Length, 30))}");
            lock (_lock)
            {
                _db.Upsert(item);

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

                if (_cache.Count > 500)
                {
                    var lastUnpinned = _cache.FindLastIndex(c => !c.IsPinned);
                    if (lastUnpinned >= 0) _cache.RemoveAt(lastUnpinned);
                }
            }

            ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
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

            if (filtered.Count == 0)
            {
                var empty = new ListItem
                {
                    Title = string.IsNullOrWhiteSpace(_searchText) ? "剪切板历史为空" : $"未找到与 \"{_searchText}\" 匹配的记录",
                    Subtitle = "复制任意文本或图片即可自动记录到历史",
                    Icon = new IconInfo("\uE9CE")
                };
                return new IListItem[] { empty };
            }

            var list = new List<IListItem>(filtered.Count);
            foreach (var item in filtered)
            {
                list.Add(CreateListItem(item));
            }

            return list.ToArray();
        }

        private ListItem CreateListItem(ClipItem item)
        {
            var li = new ListItem
            {
                Section = item.IsPinned ? "📌 置顶记录" : "🕒 最近历史"
            };

            if (item.IsPinned)
            {
                li.Tags = new ITag[] { new Tag("已置顶", "此项已固定在顶部", new IconInfo("\uE718")) };
            }

            if (item.Type == ClipType.Text)
            {
                var preview = item.Content.Trim().Replace("\r\n", " ").Replace("\n", " ");
                if (preview.Length > 80) preview = preview.Substring(0, 80) + "...";
                li.Title = preview;
                li.Subtitle = item.GetSubtitle();
                li.Icon = new IconInfo("\uE8C8");

                li.Details = new Details
                {
                    Title = "文本剪切板内容",
                    Body = $"**复制时间**: {item.CreatedAt:yyyy-MM-dd HH:mm:ss}  \n**长度**: {item.CharCount} 字符  \n\n---\n\n```text\n{item.Content}\n```"
                };
            }
            else
            {
                li.Title = $"[图片] {item.ImageWidth}x{item.ImageHeight} · {item.Content}";
                li.Subtitle = item.GetSubtitle();
                li.Icon = new IconInfo("\uEB9F");

                var imgPath = !string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath)
                    ? item.ThumbnailPath
                    : item.ImagePath;

                li.Details = new Details
                {
                    Title = "图片预览",
                    HeroImage = !string.IsNullOrEmpty(imgPath) ? new IconInfo(imgPath) : null,
                    Body = $"**分辨率**: {item.ImageWidth} × {item.ImageHeight}  \n**记录时间**: {item.CreatedAt:yyyy-MM-dd HH:mm:ss}  \n**图片路径**: `{item.ImagePath}`"
                };
            }

            li.Command = new ActionCommand("粘贴", () =>
            {
                ExtLog.Info($"Pasting item {item.Id}");
                KeyboardSimulator.SetToClipboard(item, _watcher);
                KeyboardSimulator.SendCtrlV();
                return CommandResult.Dismiss();
            });

            var more = new List<IContextItem>
            {
                new ActionCommand("仅复制到剪切板 (不粘贴)", () =>
                {
                    ExtLog.Info($"Copying item {item.Id} without paste");
                    KeyboardSimulator.SetToClipboard(item, _watcher);
                    return CommandResult.Dismiss();
                }, new IconInfo("\uE8C8")),

                new ActionCommand(item.IsPinned ? "取消置顶" : "置顶此条记录", () =>
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
                }, new IconInfo("\uE718")),

                new ActionCommand("从历史中删除", () =>
                {
                    _db.Delete(item.Id);
                    lock (_lock)
                    {
                        _cache.RemoveAll(c => c.Id == item.Id);
                    }
                    ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                    return CommandResult.KeepOpen();
                }, new IconInfo("\uE74D"), isCritical: true),

                new ActionCommand("清空未置顶历史", () =>
                {
                    _db.ClearAll(preservePinned: true);
                    lock (_lock)
                    {
                        _cache.RemoveAll(c => !c.IsPinned);
                    }
                    ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
                    return CommandResult.KeepOpen();
                }, new IconInfo("\uE72C"), isCritical: true)
            };

            li.MoreCommands = more.ToArray();
            return li;
        }
    }
}
