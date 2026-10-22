using System;

namespace SlinClip.Models;

public class ClipItem
{
    public long Id { get; set; }
    public ClipType Type { get; set; }

    /// <summary>
    /// 文本内容（若是图片，则为图片元数据信息如 "1920x1080 PNG"）
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 用于内存极速检索的文本（小写化，便于忽略大小写过滤）
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// 内容唯一指纹，用于连续复制去重
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// 图片原始大图文件路径（仅图片有效）
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>
    /// 图片轻量缩略图路径（供 PowerToys Run IcoPath 直接显示）
    /// </summary>
    public string? ThumbnailPath { get; set; }

    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int CharCount { get; set; }

    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 获取友好的时间显示
    /// </summary>
    public string GetFriendlyTime()
    {
        var diff = DateTime.Now - UpdatedAt;
        if (diff.TotalSeconds < 60) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} 天前";
        return UpdatedAt.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// 获取友好的列表副标题
    /// </summary>
    public string GetSubtitle()
    {
        var pinnedPrefix = IsPinned ? "📌 [置顶] " : "";
        if (Type == ClipType.Image)
        {
            return $"{pinnedPrefix}图片 · {ImageWidth}x{ImageHeight} · {GetFriendlyTime()}";
        }

        var lines = Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
        return $"{pinnedPrefix}文本 · {CharCount} 字 ({lines} 行) · {GetFriendlyTime()}";
    }
}
