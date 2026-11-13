using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("余电 · Peripheral Charge")]
[assembly: AssemblyDescription("2.4 GHz 外设电量托盘")]
[assembly: AssemblyProduct("Peripheral Charge")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace PeripheralCharge
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool openDetails = args.Length == 1 && args[0] == "--details";
            if (args.Length > 0 && !openDetails) return Command(args);
            bool first;
            using (var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PeripheralCharge.ShowDetails"))
            using (var instance = new Mutex(true, @"Local\PeripheralCharge.Tray", out first))
            {
                if (!first) { showRequest.Set(); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var app = new Tray(showRequest, openDetails)) Application.Run(app);
            }
            return 0;
        }

        private static int Command(string[] args)
        {
            if (Console.IsOutputRedirected)
            {
                var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                output.AutoFlush = true;
                Console.SetOut(output);
            }
            try
            {
                if (args.Length == 1 && args[0] == "--list")
                {
                    Console.WriteLine("USB ID      PAGE:USAGE  IN/OUT/FEATURE  PRODUCT");
                    foreach (Endpoint e in WindowsHid.Enumerate(true))
                        Console.WriteLine("{0}   {1:X4}:{2:X4}   {3}/{4}/{5}   {6}", e.Id, e.Page, e.Usage,
                            e.InputSize, e.OutputSize, e.FeatureSize, e.Product.Replace('\r', ' ').Replace('\n', ' '));
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--status")
                {
                    Snapshot s = Monitor.Scan();
                    if (s.Devices.Count == 0) Console.WriteLine(s.EmptyMessage);
                    foreach (DeviceStatus d in s.Devices) Console.WriteLine("{0}  {1}  {2}", d.Id, d.Name, d.Battery.Display);
                    return s.Lowest.HasValue ? 0 : 2;
                }
                Console.WriteLine("PeripheralCharge.exe [--details | --status | --list | --help]");
                return args.Length == 1 && args[0] == "--help" ? 0 : 64;
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException || ex is Win32Exception || ex is IOException)) throw;
                Console.Error.WriteLine("HID scan failed: " + ex.Message);
                return 1;
            }
        }
    }
}
