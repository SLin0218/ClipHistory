using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace SlinClipCmdPal
{
    public class Program
    {
        [MTAThread]
        public static void Main(string[] args)
        {
            ExtLog.Info($"Program.Main started with args: {string.Join(" ", args)}");

            if (args.Length > 0 && args[0].Equals("-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase))
            {
                global::Shmuelie.WinRTServer.ComServer server = new();
                var extensionDisposedEvent = new ManualResetEvent(false);

                var extensionInstance = new ClipExtension(extensionDisposedEvent);
                server.RegisterClass<ClipExtension, IExtension>(() => extensionInstance);

                ExtLog.Info("ComServer.Start() calling...");
                server.Start();
                ExtLog.Info("ComServer running, waiting for extensionDisposedEvent...");

                extensionDisposedEvent.WaitOne();

                ExtLog.Info("extensionDisposedEvent signaled, stopping server...");
                server.Stop();
                server.UnsafeDispose();
            }
            else
            {
                Console.WriteLine("ClipHistory CmdPal Extension (Standalone)");
                var evt = new ManualResetEvent(false);
                var ext = new ClipExtension(evt);
                var provider = (ClipCommandProvider?)ext.GetProvider(ProviderType.Commands);
                Console.WriteLine($"TopLevelCommands count: {provider?.TopLevelCommands().Length}");
                ext.Dispose();
            }
        }
    }
}
