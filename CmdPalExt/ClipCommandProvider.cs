using System;
using Windows.Foundation;
using Microsoft.CommandPalette.Extensions;

namespace SlinClipCmdPal
{
    public sealed class ClipCommandProvider : ICommandProvider
    {
        public string Id => "SlinClip.CommandProvider";
        public string DisplayName => "Slin-Clip 剪切板历史";
        public IIconInfo Icon => new IconInfo("\uE8C8");
        public ICommandSettings? Settings => null;
        public bool Frozen => false;

        private readonly ClipHistoryPage _historyPage;
        private IExtensionHost? _host;

        public event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged;

        public ClipCommandProvider(ClipHistoryPage historyPage)
        {
            _historyPage = historyPage;
            ExtLog.Info("ClipCommandProvider initialized.");
        }

        public void InitializeWithHost(IExtensionHost host)
        {
            ExtLog.Info("ClipCommandProvider.InitializeWithHost called.");
            _host = host;
        }

        public ICommandItem[] TopLevelCommands()
        {
            ExtLog.Info("ClipCommandProvider.TopLevelCommands called.");
            var topCmd = new ListItem
            {
                Title = "剪切板历史 (Slin-Clip)",
                Subtitle = "查看并检索历史复制的文本与图片，支持置顶与一键粘贴",
                Icon = new IconInfo("\uE8C8"),
                Command = _historyPage,
                Details = new Details
                {
                    Title = "Slin-Clip 剪切板扩展",
                    Body = "### 欢迎使用 Slin-Clip\n\n* **超快检索**: 基于 SQLite WAL 高速存储与内存缓存\n* **图文兼备**: 自动生成图片缩略图，文本全量检索\n* **快捷粘贴**: 回车即选即粘，自动还原剪切板"
                }
            };

            return new ICommandItem[] { topCmd };
        }

        public IFallbackCommandItem[] FallbackCommands() => Array.Empty<IFallbackCommandItem>();

        public ICommand? GetCommand(string id)
        {
            ExtLog.Info($"ClipCommandProvider.GetCommand called with id: {id}");
            if (id == _historyPage.Id)
            {
                return _historyPage;
            }
            return null;
        }

        public void Dispose()
        {
            ExtLog.Info("ClipCommandProvider Dispose called.");
        }
    }
}
