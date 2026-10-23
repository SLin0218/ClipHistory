using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using SlinClip.Services;

namespace SlinClipCmdPal
{
    [Guid("CA55B77B-582E-47C0-8B41-C68427BFB331")]
    public sealed class ClipExtension : IExtension, IDisposable
    {
        private static readonly string AppDataDir = GetAppDataDirectory();
        private static readonly string DbPath = Path.Combine(AppDataDir, "clip_history.db");

        private static string GetAppDataDirectory()
        {
            var newDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerToys", "ClipHistory");
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerToys", "SlinClip");

            if (!Directory.Exists(newDir))
            {
                if (Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.CreateDirectory(newDir);
                        foreach (var file in Directory.GetFiles(oldDir))
                        {
                            var dest = Path.Combine(newDir, Path.GetFileName(file));
                            if (Path.GetFileName(file).Equals("slin_clip.db", StringComparison.OrdinalIgnoreCase))
                            {
                                dest = Path.Combine(newDir, "clip_history.db");
                            }
                            if (!File.Exists(dest)) File.Copy(file, dest);
                        }
                        foreach (var subDir in Directory.GetDirectories(oldDir))
                        {
                            var destDir = Path.Combine(newDir, Path.GetFileName(subDir));
                            if (!Directory.Exists(destDir))
                            {
                                Directory.CreateDirectory(destDir);
                                foreach (var f in Directory.GetFiles(subDir))
                                {
                                    var df = Path.Combine(destDir, Path.GetFileName(f));
                                    if (!File.Exists(df)) File.Copy(f, df);
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    Directory.CreateDirectory(newDir);
                }
            }

            return newDir;
        }

        private readonly ManualResetEvent _extensionDisposedEvent;
        private readonly DatabaseService _db;
        private readonly ThumbnailService _thumb;
        private readonly SettingsService _settingsService;
        private readonly ClipboardWatcher _watcher;
        private readonly ClipHistoryPage _historyPage;
        private readonly ClipSettingsContentPage _settingsPage;
        private readonly ClipCommandProvider _provider;

        public ClipExtension(ManualResetEvent extensionDisposedEvent)
        {
            _extensionDisposedEvent = extensionDisposedEvent;
            ExtLog.Info("ClipExtension constructor called.");
            try
            {
                Directory.CreateDirectory(AppDataDir);
                _db = new DatabaseService(DbPath);
                _thumb = new ThumbnailService(AppDataDir);
                _settingsService = new SettingsService(AppDataDir);
                _watcher = new ClipboardWatcher(_thumb, _settingsService);
                _historyPage = new ClipHistoryPage(_db, _thumb, _watcher, _settingsService);
                _settingsPage = new ClipSettingsContentPage(_historyPage, _db, _watcher, _settingsService, AppDataDir);
                _historyPage.SettingsPage = _settingsPage;
                _provider = new ClipCommandProvider(_historyPage, _settingsPage);
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
            return providerType switch
            {
                ProviderType.Commands => _provider,
                _ => null
            };
        }

        public void Dispose()
        {
            ExtLog.Info($"ClipExtension Dispose called from:\n{Environment.StackTrace}");
            try { _historyPage?.Dispose(); } catch { }
            try { _settingsPage?.Dispose(); } catch { }
            try { _watcher?.Dispose(); } catch { }
            try { _db?.Dispose(); } catch { }
            try { _provider?.Dispose(); } catch { }
            _extensionDisposedEvent.Set();
        }
    }
}
