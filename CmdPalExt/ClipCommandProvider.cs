using System;
using Windows.Foundation;
using Microsoft.CommandPalette.Extensions;
using SlinClip.Services;

namespace SlinClipCmdPal
{
    public sealed class ClipCommandProvider : ICommandProvider, IDisposable
    {
        public string Id => "ClipHistory.CommandProvider";
        public string DisplayName => I18n.T("ClipHistory 剪切板历史", "ClipHistory Clipboard History");
        public IIconInfo Icon => IconInfo.FromAsset("icon.svg");
        public ICommandSettings? Settings { get; }
        public bool Frozen => false;

        private readonly ClipHistoryPage _historyPage;
        private readonly ClipSettingsContentPage _settingsPage;
        private IExtensionHost? _host;

        public event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged;

        public ClipCommandProvider(ClipHistoryPage historyPage, ClipSettingsContentPage settingsPage)
        {
            _historyPage = historyPage;
            _settingsPage = settingsPage;
            Settings = new CommandSettings(_settingsPage);
            ExtLog.Info("ClipCommandProvider initialized with official ICommandSettings.");
        }

        public void InitializeWithHost(IExtensionHost host)
        {
            ExtLog.Info("ClipCommandProvider.InitializeWithHost called.");
            _host = host;
        }

        public ICommandItem[] TopLevelCommands()
        {
            ExtLog.Info("ClipCommandProvider.TopLevelCommands called.");
            var topCmd = new CommandItem(_historyPage)
            {
                Title = I18n.T("剪切板历史 (ClipHistory)", "Clipboard History (ClipHistory)"),
                Subtitle = I18n.T("查看并检索历史复制的文本与图片，支持置顶与快捷管理", "View and search copied text and images with pin and shortcut management"),
                Icon = IconInfo.FromAsset("icon.svg")
            };

            return new ICommandItem[] { topCmd };
        }

        public IFallbackCommandItem[] FallbackCommands() => Array.Empty<IFallbackCommandItem>();

        public ICommand? GetCommand(string id)
        {
            ExtLog.Info($"ClipCommandProvider.GetCommand called with id: {id}");
            if (string.Equals(id, _historyPage.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "ClipHistoryPage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "SlinClipHistoryPage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "ch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "sc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "clip", StringComparison.OrdinalIgnoreCase))
            {
                return _historyPage;
            }

            if (string.Equals(id, _settingsPage.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "ClipHistorySettingsPage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "SlinClipSettingsPage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "ch-set", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "sc-settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "sc-set", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "scs", StringComparison.OrdinalIgnoreCase))
            {
                return _settingsPage;
            }

            return null;
        }

        public void Dispose()
        {
            ExtLog.Info("ClipCommandProvider Dispose called.");
        }
    }
}
