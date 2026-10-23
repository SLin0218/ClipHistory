using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace SlinClip.Services;

public class ThumbnailService
{
    private readonly string _imagesDir;
    private readonly string _thumbsDir;
    private readonly string _previewsDir;

    public ThumbnailService(string baseDataDir)
    {
        _imagesDir = Path.Combine(baseDataDir, "Images");
        _thumbsDir = Path.Combine(baseDataDir, "Thumbs");
        _previewsDir = Path.Combine(baseDataDir, "Previews");

        if (!Directory.Exists(_imagesDir)) Directory.CreateDirectory(_imagesDir);
        if (!Directory.Exists(_thumbsDir)) Directory.CreateDirectory(_thumbsDir);
        if (!Directory.Exists(_previewsDir)) Directory.CreateDirectory(_previewsDir);
    }

    /// <summary>
    /// 保存剪贴板图片并生成高效缩略图及卡片自适应预览图
    /// </summary>
    public (string rawPath, string thumbPath, string hash, int width, int height) ProcessAndSaveImage(BitmapSource source)
    {
        // 修复 Windows 截图工具 (GDI 32位位图) Alpha 值为 0 导致的图片完全空白/透明问题
        source = FixAlphaIfAllZero(source);

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
        var prevPath = Path.Combine(_previewsDir, $"prev_{hash}.png");

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

        // 若预览图不存在则生成详情卡片适屏预览图 (最大宽 440px，高 340px)
        if (!File.Exists(prevPath))
        {
            CreateScaledImage(source, prevPath, maxWidth: 440, maxHeight: 340);
        }

        return (rawPath, thumbPath, hash, width, height);
    }

    /// <summary>
    /// 获取或按需生成适应 CmdPal 详情卡片尺寸 (最大 440x340) 的缩放预览图
    /// </summary>
    public string GetOrCreatePreviewImage(string? rawImagePath)
    {
        if (string.IsNullOrEmpty(rawImagePath) || !File.Exists(rawImagePath))
        {
            return rawImagePath ?? string.Empty;
        }

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(rawImagePath);
            string hash = fileName.StartsWith("img_") ? fileName.Substring(4) : fileName;
            var prevPath = Path.Combine(_previewsDir, $"prev_{hash}.png");

            if (File.Exists(prevPath))
            {
                return prevPath;
            }

            // 若缩放预览图不存在，动态生成一次并缓存落盘（全内存读取，杜绝文件句柄占用）
            var rawBytes = File.ReadAllBytes(rawImagePath);
            using var stream = new MemoryStream(rawBytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                CreateScaledImage(frame, prevPath, maxWidth: 440, maxHeight: 340);
                return prevPath;
            }
        }
        catch
        {
            // 降级使用原图路径
        }

        return rawImagePath;
    }

    /// <summary>
    /// 删除条目相关的所有物理图像文件（原图、轻量缩略图、适屏预览图）
    /// </summary>
    public void DeleteImageFiles(string? rawImagePath, string? thumbPath = null)
    {
        var filesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(rawImagePath))
        {
            filesToDelete.Add(rawImagePath);
            var fileName = Path.GetFileNameWithoutExtension(rawImagePath);
            string hash = fileName.StartsWith("img_") ? fileName.Substring(4) : fileName;
            filesToDelete.Add(Path.Combine(_thumbsDir, $"thumb_{hash}.png"));
            filesToDelete.Add(Path.Combine(_previewsDir, $"prev_{hash}.png"));
        }

        if (!string.IsNullOrEmpty(thumbPath))
        {
            filesToDelete.Add(thumbPath);
            var fileName = Path.GetFileNameWithoutExtension(thumbPath);
            string hash = fileName.StartsWith("thumb_") ? fileName.Substring(6) : fileName;
            filesToDelete.Add(Path.Combine(_imagesDir, $"img_{hash}.png"));
            filesToDelete.Add(Path.Combine(_previewsDir, $"prev_{hash}.png"));
        }

        DeleteFilesSafe(filesToDelete);
    }

    public static void DeleteFilesSafe(IEnumerable<string> files)
    {
        var list = files.Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();
        if (list.Count == 0) return;

        System.Threading.Tasks.Task.Run(async () =>
        {
            foreach (var file in list)
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                        break;
                    }
                    catch (IOException)
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        });
    }

    /// <summary>
    /// 修复 Windows 剪贴板截图 Alpha 均为 0 的问题。
    /// GDI 32位 DIB 位图第4字节为保留字节(通常为 0x00)，WPF 解码为 Bgra32 时会把 Alpha 视作 0。
    /// 若整张位图的所有像素 Alpha 均为 0，则将 Alpha 全部设为 255 (完全不透明)。
    /// </summary>
    public static BitmapSource FixAlphaIfAllZero(BitmapSource source)
    {
        if (source == null) return source!;

        // 统一转为 Bgra32
        var formattedSource = new FormatConvertedBitmap();
        formattedSource.BeginInit();
        formattedSource.Source = source;
        formattedSource.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
        formattedSource.EndInit();

        int width = formattedSource.PixelWidth;
        int height = formattedSource.PixelHeight;
        if (width <= 0 || height <= 0) return source;

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        formattedSource.CopyPixels(pixels, stride, 0);

        bool hasNonZeroAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasNonZeroAlpha = true;
                break;
            }
        }

        if (!hasNonZeroAlpha && pixels.Length > 0)
        {
            // 所有像素 Alpha 均为 0，强制设置为 255 (完全不透明)
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            var fixedBitmap = BitmapSource.Create(
                width,
                height,
                formattedSource.DpiX > 0 ? formattedSource.DpiX : 96,
                formattedSource.DpiY > 0 ? formattedSource.DpiY : 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            fixedBitmap.Freeze();
            return fixedBitmap;
        }

        if (formattedSource.CanFreeze && !formattedSource.IsFrozen)
        {
            formattedSource.Freeze();
        }
        return formattedSource;
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

    private void CreateScaledImage(BitmapSource source, string savePath, int maxWidth, int maxHeight)
    {
        double scaleX = (double)maxWidth / source.PixelWidth;
        double scaleY = (double)maxHeight / source.PixelHeight;
        double scale = Math.Min(1.0, Math.Min(scaleX, scaleY));

        BitmapSource outputSource = source;
        if (scale < 1.0)
        {
            var transformed = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
            if (transformed.CanFreeze && !transformed.IsFrozen)
            {
                transformed.Freeze();
            }
            outputSource = transformed;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(outputSource));
        using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }
}
