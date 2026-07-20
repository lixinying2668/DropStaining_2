using System;
using System.Threading;

namespace Stainer.SoconBridge
{
    internal static class Program
    {
        private const string SelfTestArgument = "--self-test";
        private const string EnableRealReadOnlyArgument = "--enable-real-read-only";

        public static int Main(string[] args)
        {
            if (args != null && args.Length == 1 && string.Equals(args[0], SelfTestArgument, StringComparison.OrdinalIgnoreCase))
            {
                return SelfTestRunner.Run(Console.Out);
            }

            var enableRealReadOnly = args != null
                && args.Length == 1
                && string.Equals(args[0], EnableRealReadOnlyArgument, StringComparison.OrdinalIgnoreCase);

            if (args != null && args.Length > 0 && !enableRealReadOnly)
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
                    enableRealReadOnly);
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
                    Console.WriteLine("Bridge stopped.");
                }
            }
        }
    }
}
