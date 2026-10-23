using System;
using System.IO;
using Windows.Foundation;
using Windows.Storage.Streams;
using Microsoft.CommandPalette.Extensions;

namespace SlinClipCmdPal
{
    public static class ExtLog
    {
        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerToys", "ClipHistory", "cmdpal_extension.log");
        private static readonly object LockObj = new();

        public static void Info(string message)
        {
            try
            {
                lock (LockObj)
                {
                    var dir = Path.GetDirectoryName(LogFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var fi = new FileInfo(LogFile);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                    {
                        var lines = File.ReadAllLines(LogFile);
                        var keepLines = System.Linq.Enumerable.Skip(lines, Math.Max(0, lines.Length - 1000));
                        File.WriteAllLines(LogFile, keepLines);
                    }

                    File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
                }
            }
            catch { }
        }
    }

    public sealed class GoToPageArgs : IGoToPageArgs, ICommandResultArgs
    {
        public string PageId { get; }
        public NavigationMode NavigationMode { get; }

        public GoToPageArgs(string pageId, NavigationMode mode = NavigationMode.Push)
        {
            PageId = pageId;
            NavigationMode = mode;
        }
    }

    public sealed class ConfirmationArgs : IConfirmationArgs, ICommandResultArgs
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ICommand PrimaryCommand { get; set; }
        public bool IsPrimaryCommandCritical { get; set; } = true;

        public ConfirmationArgs(string title, string description, ICommand primaryCommand, bool isCritical = true)
        {
            Title = title;
            Description = description;
            PrimaryCommand = primaryCommand;
            IsPrimaryCommandCritical = isCritical;
        }
    }

    public sealed class CommandResult : ICommandResult
    {
        public CommandResultKind Kind { get; }
        public ICommandResultArgs? Args { get; }

        public CommandResult(CommandResultKind kind, ICommandResultArgs? args = null)
        {
            Kind = kind;
            Args = args;
        }

        public static CommandResult Dismiss() => new(CommandResultKind.Dismiss);
        public static CommandResult KeepOpen() => new(CommandResultKind.KeepOpen);
        public static CommandResult Hide() => new(CommandResultKind.Hide);
        public static CommandResult GoToPage(string pageId) => new(CommandResultKind.GoToPage, new GoToPageArgs(pageId));
        public static CommandResult Confirm(IConfirmationArgs args) => new(CommandResultKind.Confirm, args);
    }

    public sealed class IconData : IIconData
    {
        public string Icon { get; set; }
        public IRandomAccessStreamReference? Data { get; set; }

        public IconData(string icon, IRandomAccessStreamReference? data = null)
        {
            Icon = icon ?? string.Empty;
            Data = data;
        }
    }

    public sealed class IconInfo : IIconInfo
    {
        public IIconData Light { get; }
        public IIconData Dark { get; }

        public IconInfo(string icon)
        {
            Light = new IconData(icon);
            Dark = new IconData(icon);
        }

        public IconInfo(string light, string dark)
        {
            Light = new IconData(light);
            Dark = new IconData(dark);
        }

        public IconInfo(IIconData light, IIconData dark)
        {
            Light = light;
            Dark = dark;
        }

        public static IconInfo FromFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return new IconInfo("\uEB9F");
            }

            return new IconInfo(new IconData(filePath), new IconData(filePath));
        }

        public static IconInfo FromAsset(string fileName)
        {
            try
            {
                var fullPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
                if (File.Exists(fullPath))
                {
                    return FromFilePath(fullPath);
                }
            }
            catch (Exception ex)
            {
                ExtLog.Info($"FromAsset error for {fileName}: {ex}");
            }
            return new IconInfo("\uE8C8");
        }
    }

    public sealed class PropChangedEventArgs : IPropChangedEventArgs
    {
        public string PropertyName { get; }
        public PropChangedEventArgs(string propName) => PropertyName = propName;
    }

    public sealed class ItemsChangedEventArgs : IItemsChangedEventArgs
    {
        public int TotalItems { get; }
        public ItemsChangedEventArgs(int totalItems = -1) => TotalItems = totalItems;
    }

    public sealed class ActionCommand : IInvokableCommand, ICommand, INotifyPropChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }

        private readonly Func<ICommandResult> _action;

        public ActionCommand(string name, Func<ICommandResult> action, IIconInfo? icon = null)
        {
            Name = name;
            _action = action;
            Icon = icon;
        }

        public ICommandResult Invoke(object sender)
        {
            ExtLog.Info($"ActionCommand.Invoke: {Name}");
            return _action();
        }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
    }

    public sealed class CommandContextItem : ICommandContextItem, ICommandItem, IContextItem, INotifyPropChanged
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }
        public ICommand? Command { get; set; }
        public IContextItem[] MoreCommands { get; set; } = Array.Empty<IContextItem>();
        public bool IsCritical { get; set; } = false;
        public KeyChord RequestedShortcut { get; set; } = default;

        public CommandContextItem(string title, Func<ICommandResult> action, IIconInfo? icon = null, bool isCritical = false)
        {
            Title = title;
            Icon = icon;
            IsCritical = isCritical;
            Command = new ActionCommand(title, action, icon);
        }

        public CommandContextItem(ICommand command, string? title = null, IIconInfo? icon = null, bool isCritical = false)
        {
            Command = command;
            Title = title ?? command?.Name ?? string.Empty;
            Icon = icon ?? command?.Icon;
            IsCritical = isCritical;
        }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
    }

    public sealed class Tag : ITag
    {
        public string Text { get; set; } = string.Empty;
        public string ToolTip { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }
        public OptionalColor Foreground => default;
        public OptionalColor Background => default;

        public Tag(string text, string toolTip = "", IIconInfo? icon = null)
        {
            Text = text;
            ToolTip = toolTip;
            Icon = icon;
        }
    }

    public sealed class Details : IDetails
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public IIconInfo? HeroImage { get; set; }
        public IDetailsElement[] Metadata { get; set; } = Array.Empty<IDetailsElement>();
    }

    public sealed class CommandItem : ICommandItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }
        public ICommand? Command { get; set; }
        public IContextItem[] MoreCommands { get; set; } = Array.Empty<IContextItem>();

        public CommandItem(ICommand command)
        {
            Command = command;
        }

        public CommandItem() { }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
    }

    public sealed class ListItem : IListItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }
        public ICommand? Command { get; set; }
        public IContextItem[] MoreCommands { get; set; } = Array.Empty<IContextItem>();
        public string Section { get; set; } = string.Empty;
        public string TextToSuggest { get; set; } = string.Empty;
        public ITag[] Tags { get; set; } = Array.Empty<ITag>();
        public IDetails? Details { get; set; }

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
    }

    public sealed class CommandSettings : ICommandSettings
    {
        public IContentPage SettingsPage { get; }

        public CommandSettings(IContentPage settingsPage)
        {
            SettingsPage = settingsPage;
        }
    }

    public class ContentPage : IContentPage, IPage, ICommand, INotifyPropChanged, INotifyItemsChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }
        public string Title { get; set; } = string.Empty;
        public OptionalColor AccentColor => default;
        public bool IsLoading { get; set; } = false;
        public IDetails Details { get; set; } = new Details();
        public IContextItem[] Commands { get; set; } = Array.Empty<IContextItem>();

        public virtual IContent[] GetContent() => Array.Empty<IContent>();

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;
        public event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged;

        protected void RaisePropChanged(string propName) =>
            PropChanged?.Invoke(this, new PropChangedEventArgs(propName));

        protected void RaiseItemsChanged() =>
            ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(-1));
    }

    public class FormContent : IFormContent
    {
        public string TemplateJson { get; set; } = string.Empty;
        public string DataJson { get; set; } = string.Empty;
        public string StateJson { get; set; } = string.Empty;

        public event TypedEventHandler<object, IPropChangedEventArgs>? PropChanged;

        public virtual ICommandResult SubmitForm(string inputs, string data)
        {
            return CommandResult.KeepOpen();
        }

        protected void RaisePropChanged(string propName) =>
            PropChanged?.Invoke(this, new PropChangedEventArgs(propName));
    }
}
