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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerToys", "SlinClip", "cmdpal_extension.log");
        private static readonly object LockObj = new();

        public static void Info(string message)
        {
            try
            {
                lock (LockObj)
                {
                    var dir = Path.GetDirectoryName(LogFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
                }
            }
            catch { }
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
    }

    public sealed class IconData : IIconData
    {
        public string Icon { get; set; }
        public IRandomAccessStreamReference? Data => null;

        public IconData(string icon) => Icon = icon ?? string.Empty;
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

    public sealed class ActionCommand : IInvokableCommand, ICommandContextItem, IContextItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public IIconInfo? Icon { get; set; }
        public bool IsCritical { get; set; } = false;
        public KeyChord RequestedShortcut { get; set; } = default;

        public string Title => Name;
        public string Subtitle => string.Empty;
        public ICommand Command => this;
        public IContextItem[] MoreCommands => Array.Empty<IContextItem>();

        private readonly Func<ICommandResult> _action;

        public ActionCommand(string name, Func<ICommandResult> action, IIconInfo? icon = null, bool isCritical = false)
        {
            Name = name;
            _action = action;
            Icon = icon;
            IsCritical = isCritical;
        }

        public ICommandResult Invoke(object sender)
        {
            ExtLog.Info($"Invoking command: {Name}");
            return _action();
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
}
