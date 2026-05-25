using System;
using Microsoft.Win32;

namespace STFormatter.UI
{
    public static class AutoStart
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "STFormatter";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key?.GetValue(AppName) != null;
                }
            }
            catch { return false; }
        }

        public static void Enable()
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location
                                 ?? typeof(AutoStart).Assembly.Location;
                if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    exePath = exePath.Replace(".dll", ".exe");
                }

                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    key?.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            catch { }
        }

        public static void Disable()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    key?.DeleteValue(AppName, false);
                }
            }
            catch { }
        }
    }
}