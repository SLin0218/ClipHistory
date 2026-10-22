using System;
using System.Collections.Generic;

namespace Wox.Plugin;

public interface IPlugin
{
    void Init(PluginInitContext context);
    List<Result> Query(Query query);
    string Name { get; }
    string Description { get; }
}

public interface IContextMenu
{
    List<ContextMenuResult> LoadContextMenus(Result selectedResult);
}

public interface IPublicAPI
{
    void ChangeQuery(string query, bool requery = false);
    void ReQuery();
}

public class PluginInitContext
{
    public PluginMetadata CurrentPluginMetadata { get; set; } = new();
    public IPublicAPI API { get; set; } = null!;
}

public class PluginMetadata
{
    public string ID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PluginDirectory { get; set; } = string.Empty;
    public string ActionKeyword { get; set; } = string.Empty;
}

public class Query
{
    public string RawQuery { get; set; } = string.Empty;
    public string Search { get; set; } = string.Empty;
    public string ActionKeyword { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
}

public class ActionContext
{
    public SpecialKeyState SpecialKeyState { get; set; } = new();
}

public class SpecialKeyState
{
    public bool CtrlPressed { get; set; }
    public bool ShiftPressed { get; set; }
    public bool AltPressed { get; set; }
    public bool WinPressed { get; set; }
}

public class Result
{
    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;
    public string IcoPath { get; set; } = string.Empty;
    public Func<ActionContext, bool>? Action { get; set; }
    public object? ContextData { get; set; }
    public int Score { get; set; }
}

public class ContextMenuResult
{
    public string PluginDirectory { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;
    public string FontFamily { get; set; } = string.Empty;
    public Func<ActionContext, bool>? Action { get; set; }
    public System.Windows.Input.Key AcceleratorKey { get; set; }
    public System.Windows.Input.ModifierKeys AcceleratorModifiers { get; set; }
}
