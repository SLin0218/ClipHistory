using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using SlinClip.Services;

namespace SlinClipCmdPal
{
    public sealed class ClipSettingsContentPage : ContentPage, IInvokableCommand, IDisposable
    {
        private readonly ClipSettingsForm _form;

        public ClipSettingsContentPage(
            ClipHistoryPage historyPage,
            DatabaseService db,
            ClipboardWatcher watcher,
            SettingsService settingsService,
            string appDataDir)
        {
            Id = "ClipHistorySettingsPage";
            Name = "ClipHistory Settings";
            Title = I18n.T("ClipHistory 扩展设置", "ClipHistory Extension Settings");
            Icon = new IconInfo("\uE713");

            _form = new ClipSettingsForm(historyPage, db, watcher, settingsService, appDataDir);
        }

        public override IContent[] GetContent() => new IContent[] { _form };

        public ICommandResult Invoke(object sender)
        {
            ExtLog.Info("ClipSettingsContentPage.Invoke called - navigating to settings");
            return CommandResult.GoToPage(Id);
        }

        public void Dispose() => _form.Dispose();
    }

    public sealed class ClipSettingsForm : FormContent, IDisposable
    {
        private readonly ClipHistoryPage _historyPage;
        private readonly DatabaseService _db;
        private readonly ClipboardWatcher _watcher;
        private readonly SettingsService _settingsService;
        private readonly string _appDataDir;
        private readonly Action<AppSettings> _onSettingsChangedHandler;
        private string _statusNotice = I18n.T("设置就绪", "Settings ready");

        public ClipSettingsForm(
            ClipHistoryPage historyPage,
            DatabaseService db,
            ClipboardWatcher watcher,
            SettingsService settingsService,
            string appDataDir)
        {
            _historyPage = historyPage;
            _db = db;
            _watcher = watcher;
            _settingsService = settingsService;
            _appDataDir = appDataDir;

            TemplateJson = BuildTemplateJson();
            UpdateDataJson();

            _onSettingsChangedHandler = _ =>
            {
                UpdateDataJson();
                RaisePropChanged(nameof(DataJson));
            };
            _settingsService.OnSettingsChanged += _onSettingsChangedHandler;
        }

        public void Dispose()
        {
            _settingsService.OnSettingsChanged -= _onSettingsChangedHandler;
        }

        private void UpdateDataJson()
        {
            var s = _settingsService.Settings;
            var dataObj = new
            {
                maxHistory = s.MaxHistoryCount.ToString(),
                maxSize = s.MaxContentSizeKB.ToString(),
                status = _statusNotice,
                unpinnedCount = _historyPage.UnpinnedCount,
                totalCount = _historyPage.TotalCount
            };
            DataJson = JsonSerializer.Serialize(dataObj);
        }

        private string BuildTemplateJson()
        {
            return $$"""
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.5",
              "body": [
                {
                  "type": "Container",
                  "items": [
                    {
                      "type": "TextBlock",
                      "text": "{{I18n.T("最大保存条目数", "Maximum History Items")}}",
                      "size": "Medium",
                      "weight": "Bolder"
                    },
                    {
                      "type": "Input.ChoiceSet",
                      "id": "maxHistory",
                      "style": "compact",
                      "value": "${maxHistory}",
                      "choices": [
                        { "title": "{{I18n.T("100 条 (极速省内存，极低系统开销)", "100 items (Ultra-fast, lowest memory)")}}", "value": "100" },
                        { "title": "{{I18n.T("300 条 (日常轻度办公记录)", "300 items (Light daily usage)")}}", "value": "300" },
                        { "title": "{{I18n.T("500 条 (推荐默认，毫秒级响应与最佳性能平衡)", "500 items (Recommended default, best balance)")}}", "value": "500" },
                        { "title": "{{I18n.T("1000 条 (深度高频保存，适合程序员/研究员)", "1000 items (Heavy usage for developers/researchers)")}}", "value": "1000" },
                        { "title": "{{I18n.T("2000 条 (海量存档模式)", "2000 items (Massive archive mode)")}}", "value": "2000" }
                      ]
                    },
                    {
                      "type": "TextBlock",
                      "text": "{{I18n.T("平衡 SQLite 索引速度与内存占用，超额旧记录将根据 LRU 策略自动淘汰（固定置顶项受保护，永不淘汰）。", "Balances SQLite indexing speed and memory usage. Old records beyond limit are pruned by LRU policy (pinned items are protected and never pruned).")}}",
                      "size": "Small",
                      "isSubtle": true,
                      "wrap": true,
                      "spacing": "Small"
                    }
                  ]
                },
                {
                  "type": "Container",
                  "spacing": "Medium",
                  "separator": true,
                  "items": [
                    {
                      "type": "TextBlock",
                      "text": "{{I18n.T("单条内容大小上限", "Max Content Size Limit")}}",
                      "size": "Medium",
                      "weight": "Bolder"
                    },
                    {
                      "type": "Input.ChoiceSet",
                      "id": "maxSize",
                      "style": "compact",
                      "value": "${maxSize}",
                      "choices": [
                        { "title": "{{I18n.T("256 KB (超轻量过滤)", "256 KB (Ultra lightweight)")}}", "value": "256" },
                        { "title": "{{I18n.T("512 KB (轻量文本过滤)", "512 KB (Lightweight text)")}}", "value": "512" },
                        { "title": "{{I18n.T("1024 KB (1 MB, 推荐默认，可容纳数十万字代码/文章)", "1024 KB (1 MB, Recommended default, holds large code/docs)")}}", "value": "1024" },
                        { "title": "{{I18n.T("2048 KB (2 MB, 大文本模式)", "2048 KB (2 MB, Large text mode)")}}", "value": "2048" },
                        { "title": "{{I18n.T("5120 KB (5 MB, 超大图文模式)", "5120 KB (5 MB, Extra large mode)")}}", "value": "5120" }
                      ]
                    },
                    {
                      "type": "TextBlock",
                      "text": "{{I18n.T("超过此大小的内容将被自动忽略，有效杜绝误复制巨大日志、大数据文件或二进制块造成的卡顿或内存膨胀。", "Items larger than this size will be ignored to prevent lag or memory inflation when copying huge logs or files.")}}",
                      "size": "Small",
                      "isSubtle": true,
                      "wrap": true,
                      "spacing": "Small"
                    }
                  ]
                },
                {
                  "type": "Container",
                  "spacing": "Medium",
                  "separator": true,
                  "items": [
                    {
                      "type": "TextBlock",
                      "text": "{{I18n.T("状态与存储信息", "Status & Storage Information")}}",
                      "size": "Medium",
                      "weight": "Bolder"
                    },
                    {
                      "type": "TextBlock",
                      "text": "{{I18n.T("当前共有 ${totalCount} 条记录 (其中常规未置顶 ${unpinnedCount} 条) | 状态: ${status}", "Total: ${totalCount} records (unpinned: ${unpinnedCount}) | Status: ${status}")}}",
                      "color": "Accent",
                      "size": "Small",
                      "wrap": true
                    }
                  ]
                }
              ],
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "{{I18n.T("保存配置", "Save Settings")}}",
                  "data": { "action": "save" }
                },
                {
                  "type": "Action.Submit",
                  "title": "{{I18n.T("清空常规历史 (保留置顶)", "Clear Unpinned (Keep Pinned)")}}",
                  "data": { "action": "clearUnpinned" }
                },
                {
                  "type": "Action.Submit",
                  "title": "{{I18n.T("彻底清空所有历史", "Clear All History")}}",
                  "data": { "action": "clearAll" }
                },
                {
                  "type": "Action.Submit",
                  "title": "{{I18n.T("打开存储目录", "Open Storage Folder")}}",
                  "data": { "action": "openFolder" }
                }
              ]
            }
            """;
        }

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            ExtLog.Info($"SubmitForm called. inputs: {inputs}, data: {data}");
            try
            {
                string action = "save";
                if (!string.IsNullOrEmpty(data))
                {
                    try
                    {
                        var dataNode = JsonNode.Parse(data);
                        action = dataNode?["action"]?.ToString() ?? "save";
                    }
                    catch { }
                }

                if (action == "save")
                {
                    if (!string.IsNullOrEmpty(inputs))
                    {
                        var inputNode = JsonNode.Parse(inputs);
                        if (inputNode != null)
                        {
                            var mhRaw = inputNode["maxHistory"]?.ToString() ?? "500";
                            int maxHist = int.TryParse(mhRaw, out var mhVal) ? mhVal : 500;

                            var msRaw = inputNode["maxSize"]?.ToString() ?? "1024";
                            int maxSz = int.TryParse(msRaw, out var msVal) ? msVal : 1024;

                            _settingsService.Update(s =>
                            {
                                s.MaxHistoryCount = maxHist;
                                s.MaxContentSizeKB = maxSz;
                            });

                            _statusNotice = string.Format(I18n.T("已于 {0} 成功保存设置！", "Settings saved successfully at {0}!"), DateTime.Now.ToString("HH:mm:ss"));
                        }
                    }
                }
                else if (action == "clearUnpinned")
                {
                    _historyPage.ClearHistory(preservePinned: true);
                    _historyPage.ClearSystemClipboard();
                    _statusNotice = string.Format(I18n.T("已于 {0} 成功清空未置顶历史与系统剪切板！", "Unpinned history and clipboard cleared at {0}!"), DateTime.Now.ToString("HH:mm:ss"));
                }
                else if (action == "clearAll")
                {
                    _historyPage.ClearHistory(preservePinned: false);
                    _historyPage.ClearSystemClipboard();
                    _statusNotice = string.Format(I18n.T("已于 {0} 彻底清空全部历史记录！", "All history records cleared at {0}!"), DateTime.Now.ToString("HH:mm:ss"));
                }
                else if (action == "openFolder")
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", _appDataDir) { UseShellExecute = true });
                        _statusNotice = I18n.T("已打开本地存储目录", "Storage folder opened");
                    }
                    catch (Exception ex)
                    {
                        _statusNotice = string.Format(I18n.T("打开目录失败: {0}", "Failed to open folder: {0}"), ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                ExtLog.Info($"Error in SubmitForm: {ex}");
                _statusNotice = string.Format(I18n.T("操作异常: {0}", "Operation error: {0}"), ex.Message);
            }

            UpdateDataJson();
            RaisePropChanged(nameof(DataJson));
            return CommandResult.KeepOpen();
        }
    }
}
