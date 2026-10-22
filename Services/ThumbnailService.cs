using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace SlinClip.Services;

public class ThumbnailService
{
    private readonly string _imagesDir;
    private readonly string _thumbsDir;

    public ThumbnailService(string baseDataDir)
    {
        _imagesDir = Path.Combine(baseDataDir, "Images");
        _thumbsDir = Path.Combine(baseDataDir, "Thumbs");

        if (!Directory.Exists(_imagesDir)) Directory.CreateDirectory(_imagesDir);
        if (!Directory.Exists(_thumbsDir)) Directory.CreateDirectory(_thumbsDir);
    }

    /// <summary>
    /// 保存剪贴板图片并生成高效缩略图
    /// </summary>
    public (string rawPath, string thumbPath, string hash, int width, int height) ProcessAndSaveImage(BitmapSource source)
    {
        // 冻结图片对象以便跨线程安全使用
        if (source.CanFreeze && !source.IsFrozen)
        {
            source.Freeze();
        }

        int width = source.PixelWidth;
        int height = source.PixelHeight;

        // 1. 将位图转为 PNG 字节流
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        var imageBytes = ms.ToArray();

        // 2. 计算 SHA256 指纹哈希
        string hash;
        using (var sha = SHA256.Create())
        {
            var hashBytes = sha.ComputeHash(imageBytes);
            hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var rawPath = Path.Combine(_imagesDir, $"img_{hash}.png");
        var thumbPath = Path.Combine(_thumbsDir, $"thumb_{hash}.png");

        // 若大图文件不存在则落盘
        if (!File.Exists(rawPath))
        {
            File.WriteAllBytes(rawPath, imageBytes);
        }

        // 若缩略图不存在则生成轻量缩略图 (最大边 96px)
        if (!File.Exists(thumbPath))
        {
            CreateThumbnail(source, thumbPath, maxDimension: 96);
        }

        return (rawPath, thumbPath, hash, width, height);
    }

    private void CreateThumbnail(BitmapSource source, string savePath, int maxDimension)
    {
        double scale = 1.0;
        int maxSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (maxSide > maxDimension)
        {
            scale = (double)maxDimension / maxSide;
        }

        var transformed = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
        if (transformed.CanFreeze && !transformed.IsFrozen)
        {
            transformed.Freeze();
        }

        var thumbEncoder = new PngBitmapEncoder();
        thumbEncoder.Frames.Add(BitmapFrame.Create(transformed));
        using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
        thumbEncoder.Save(fs);
    }
}
