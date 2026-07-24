using System;
using System.Threading;

namespace Stainer.SoconBridge
{
    internal static class Program
    {
        private const string SelfTestArgument = "--self-test";
        private const string EnableRealReadOnlyArgument = "--enable-real-read-only";
        private const string EnableRealActionsArgument = "--enable-real-actions";

        public static int Main(string[] args)
        {
            if (args != null && args.Length == 1 && string.Equals(args[0], SelfTestArgument, StringComparison.OrdinalIgnoreCase))
            {
                return SelfTestRunner.Run(Console.Out);
            }

            var enableRealReadOnly = args != null
                && args.Length == 1
                && string.Equals(args[0], EnableRealReadOnlyArgument, StringComparison.OrdinalIgnoreCase);
            var enableRealActions = args != null
                && args.Length == 1
                && string.Equals(args[0], EnableRealActionsArgument, StringComparison.OrdinalIgnoreCase);

            if (args != null && args.Length > 0 && !enableRealReadOnly && !enableRealActions)
            {
                Console.Error.WriteLine("Unsupported Bridge startup argument.");
                return 2;
            }

            bool created;
            using (var singleInstance = new Mutex(true, @"Local\Stainer.SoconBridge", out created))
            {
                if (!created)
                {
                    Console.Error.WriteLine("Bridge already running.");
                    return 2;
                }

                var processor = BridgeRequestProcessor.CreateDefault(
                    AppDomain.CurrentDomain.BaseDirectory,
                    enableRealReadOnly,
                    enableRealActions);
                var host = new BridgeHost(BridgeHost.DefaultPipeName, processor);

                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    host.Stop();
                };

                Console.WriteLine("Bridge starting.");
                try
                {
                    host.Run();
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Bridge stopped after error: {0}", ex.GetType().Name);
                    return 1;
                }
                finally
                {
                    // Release the processor (and any active session adapter) on
                    // every exit path: normal return, Ctrl+C (host.Stop unblocks
                    // Run), and a Run exception. Cleanup failures must never mask
                    // the original exit reason, so they are swallowed here.
                    try
                    {
                        processor.Dispose();
                    }
                    catch (Exception cleanupEx)
                    {
                        Console.Error.WriteLine("Bridge cleanup error: {0}", cleanupEx.GetType().Name);
                    }

                    Console.WriteLine("Bridge stopped.");
                }
            }
        }
    }
}
