using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;
using SlinClip.Services;

namespace SlinClipCmdPal
{
    [Guid("CA55B77B-582E-47C0-8B41-C68427BFB331")]
    public sealed class ClipExtension : IExtension
    {
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerToys", "SlinClip");
        private static readonly string DbPath = Path.Combine(AppDataDir, "slin_clip.db");

        private readonly DatabaseService _db;
        private readonly ThumbnailService _thumb;
        private readonly ClipboardWatcher _watcher;
        private readonly ClipHistoryPage _historyPage;
        private readonly ClipCommandProvider _provider;

        public ClipExtension()
        {
            ExtLog.Info("ClipExtension constructor called.");
            try
            {
                Directory.CreateDirectory(AppDataDir);
                _db = new DatabaseService(DbPath);
                _thumb = new ThumbnailService(AppDataDir);
                _watcher = new ClipboardWatcher(_thumb);
                _historyPage = new ClipHistoryPage(_db, _thumb, _watcher);
                _provider = new ClipCommandProvider(_historyPage);
                ExtLog.Info("ClipExtension initialized successfully.");
            }
            catch (Exception ex)
            {
                ExtLog.Info($"Error in ClipExtension constructor: {ex}");
                throw;
            }
        }

        public object? GetProvider(ProviderType providerType)
        {
            ExtLog.Info($"GetProvider called with: {providerType}");
            if (providerType == ProviderType.Commands)
            {
                return _provider;
            }
            return null;
        }

        public void Dispose()
        {
            ExtLog.Info("ClipExtension Dispose called.");
            _watcher?.Dispose();
            _db?.Dispose();
            _provider?.Dispose();
        }
    }
}
