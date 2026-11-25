using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("CPU Health")]
[assembly: AssemblyProduct("CPU Health")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace CpuHealth
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            using (var widget = new WidgetForm { Icon = icon })
                Application.Run(widget);
        }
    }
}
