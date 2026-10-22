using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SlinClip.Models;

namespace SlinClip.Services;

public class ClipboardWatcher : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly ThumbnailService _thumbService;
    private Thread? _staThread;
    private HwndSource? _hwndSource;
    private volatile bool _isDisposed;
    private volatile bool _isInternalCopy;

    public event Action<ClipItem>? OnClipCaptured;

    public ClipboardWatcher(ThumbnailService thumbService)
    {
        _thumbService = thumbService;
        StartListenerThread();
    }

    /// <summary>
    /// 标记接下来即将发生的剪贴板变动为插件内部操作，避免循环捕获
    /// </summary>
    public void MarkInternalCopy()
    {
        _isInternalCopy = true;
    }

    private void StartListenerThread()
    {
        var readyEvent = new ManualResetEventSlim(false);

        _staThread = new Thread(() =>
        {
            var parameters = new HwndSourceParameters("SlinClipListener")
            {
                WindowStyle = 0,
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                ParentWindow = IntPtr.Zero
            };

            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);

            AddClipboardFormatListener(_hwndSource.Handle);
            readyEvent.Set();

            System.Windows.Threading.Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "SlinClipWatcherThread"
        };

        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        readyEvent.Wait(2000);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && !_isDisposed)
        {
            if (_isInternalCopy)
            {
                _isInternalCopy = false;
                return IntPtr.Zero;
            }

            // 在后台任务中安全读取剪贴板
            ThreadPool.QueueUserWorkItem(_ => ProcessClipboardContent());
        }
        return IntPtr.Zero;
    }

    private void ProcessClipboardContent()
    {
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                // 必须在 STA 线程中读取 WPF 剪贴板
                ClipItem? captured = null;
                var sta = new Thread(() =>
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            var text = Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                captured = ProcessText(text);
                            }
                        }
                        else if (Clipboard.ContainsImage())
                        {
                            var img = Clipboard.GetImage();
                            if (img != null)
                            {
                                captured = ProcessImage(img);
                            }
                        }
                    }
                    catch
                    {
                        // 访问被占用的剪贴板可能抛异常，由外层重试
                    }
                });
                sta.SetApartmentState(ApartmentState.STA);
                sta.Start();
                sta.Join(1000);

                if (captured != null)
                {
                    OnClipCaptured?.Invoke(captured);
                    break;
                }
            }
            catch
            {
                Thread.Sleep(50);
            }
        }
    }

    private ClipItem ProcessText(string text)
    {
        string hash;
        using (var sha = SHA256.Create())
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var hashBytes = sha.ComputeHash(bytes);
            hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        return new ClipItem
        {
            Type = ClipType.Text,
            Content = text,
            SearchText = text.ToLowerInvariant(),
            Hash = hash,
            CharCount = text.Length,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private ClipItem ProcessImage(BitmapSource img)
    {
        var (rawPath, thumbPath, hash, width, height) = _thumbService.ProcessAndSaveImage(img);

        return new ClipItem
        {
            Type = ClipType.Image,
            Content = $"[图片] {width}x{height}",
            SearchText = $"[image] [图片] {width}x{height}".ToLowerInvariant(),
            Hash = hash,
            ImagePath = rawPath,
            ThumbnailPath = thumbPath,
            ImageWidth = width,
            ImageHeight = height,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_hwndSource != null)
        {
            try
            {
                RemoveClipboardFormatListener(_hwndSource.Handle);
                _hwndSource.Dispose();
            }
            catch { }
        }
    }
}
