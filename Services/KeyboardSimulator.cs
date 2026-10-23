using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SlinClip.Models;
using WinForms = System.Windows.Forms;

namespace SlinClip.Services;

public static class KeyboardSimulator
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public int type;
        [FieldOffset(8)]
        public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static void LogException(string tag, Exception ex)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerToys", "SlinClip");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "cmdpal_extension.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [KeyboardSimulator.{tag} ERROR] {ex}\r\n");
        }
        catch { }
    }

    /// <summary>
    /// 清空 Windows 当前系统剪贴板内容
    /// </summary>
    public static void ClearClipboard(ClipboardWatcher? watcher = null)
    {
        watcher?.MarkInternalCopy();

        var sta = new Thread(() =>
        {
            try
            {
                WinForms.Clipboard.Clear();
            }
            catch (Exception ex)
            {
                LogException("ClearClipboard", ex);
            }
        });

        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join(2000);
    }

    /// <summary>
    /// 将条目内容写回系统剪贴板
    /// </summary>
    public static void SetToClipboard(ClipItem item, ClipboardWatcher? watcher = null)
    {
        watcher?.MarkInternalCopy();

        var sta = new Thread(() =>
        {
            try
            {
                if (item.Type == ClipType.Text)
                {
                    WinForms.Clipboard.SetDataObject(item.Content, true, 10, 50);
                }
                else if (item.Type == ClipType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    // 读取完整字节数组至内存，坚决不通过 Image.FromFile 锁定磁盘文件
                    var imageBytes = File.ReadAllBytes(item.ImagePath);
                    using var imgStream = new MemoryStream(imageBytes);
                    using var image = new System.Drawing.Bitmap(imgStream);

                    var dataObj = new WinForms.DataObject();
                    dataObj.SetData(WinForms.DataFormats.Bitmap, true, image);

                    // 补充文件路径（支持在文件资源管理器/微信/QQ/Telegram/Slack/Discord 等直接作为文件粘贴）
                    var files = new System.Collections.Specialized.StringCollection { item.ImagePath };
                    dataObj.SetFileDropList(files);

                    // 补充 PNG 原始格式流（支持浏览器/现代应用保留透明通道的粘贴）
                    var pngStream = new MemoryStream(imageBytes);
                    dataObj.SetData("PNG", false, pngStream);

                    WinForms.Clipboard.SetDataObject(dataObj, true, 10, 50);
                }
            }
            catch (Exception ex)
            {
                LogException("SetToClipboard", ex);
            }
        });

        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join(3000);
    }

    /// <summary>
    /// 模拟发送 Ctrl + V 粘贴按键
    /// </summary>
    public static void SendCtrlV()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            // 等待 PowerToys Run 窗体退隐并将焦点还给原应用
            Thread.Sleep(100);

            var inputs = new INPUT[4];

            // 1. Ctrl Down
            inputs[0] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_CONTROL } };
            // 2. V Down
            inputs[1] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_V } };
            // 3. V Up
            inputs[2] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } };
            // 4. Ctrl Up
            inputs[3] = new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        });
    }
}
