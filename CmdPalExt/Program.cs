using System;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace SlinClipCmdPal
{
    public static class Program
    {
        [STAThread]
        public static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase))
            {
                var serverDisposed = new TaskCompletionSource();
                await using var server = new ComServer();

                ClipExtension? extensionInstance = null;

                server.RegisterClass<ClipExtension, IExtension>(() =>
                {
                    extensionInstance = new ClipExtension();
                    return extensionInstance;
                });

                server.Start();

                // Keep process alive while ComServer is active
                await serverDisposed.Task;

                extensionInstance?.Dispose();
                server.Stop();
            }
            else
            {
                Console.WriteLine("========================================");
                Console.WriteLine(" Slin-Clip CmdPal Extension (Standalone)");
                Console.WriteLine("========================================");
                var ext = new ClipExtension();
                Console.WriteLine("Initialized ClipExtension successfully.");
                var provider = (ClipCommandProvider?)ext.GetProvider(ProviderType.Commands);
                var commands = provider?.TopLevelCommands();
                Console.WriteLine($"TopLevelCommands count: {commands?.Length}");
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
                ext.Dispose();
            }
        }
    }
}
