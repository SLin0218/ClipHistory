using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using SlinClip.Models;

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
                    Clipboard.SetDataObject(item.Content, true);
                }
                else if (item.Type == ClipType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    var uri = new Uri(item.ImagePath, UriKind.Absolute);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = uri;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    Clipboard.SetDataObject(bitmap, true);
                }
            }
            catch { }
        });

        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join(1000);
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
