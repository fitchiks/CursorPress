using System.Diagnostics;

namespace CursorPress;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--watch") return Watch(args);
        if (args.Contains("--restore")) return Native.ResetScheme() ? 0 : 1;
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--shutdown")) return Shutdown(args);
        ApplicationConfiguration.Initialize();
        UiText.Language = Settings.Load().Language;
        int renderIndex = Array.IndexOf(args, "--render-preview");
        if (renderIndex >= 0 && renderIndex + 1 < args.Length)
        {
            string? renderLanguage = args.Contains("--en") ? "en" : args.Contains("--ru") ? "ru" : null;
            return Screenshots.Save(args[renderIndex + 1], renderLanguage);
        }
        using var mutex = new Mutex(true, @"Local\CursorPress.App.v1", out bool first);
        if (!first)
        {
            MessageBox.Show(UiText.T("CursorPress уже работает. Откройте настройки через значок в трее.", "CursorPress is already running. Open settings from the tray icon."), "CursorPress");
            return 0;
        }
        bool smoke = args.Contains("--smoke-test") || args.Contains("--preview");
        string eventName = @"Local\CursorPress.Clean." + Guid.NewGuid().ToString("N");
        using var clean = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        if (!smoke)
        {
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--watch");
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                start.ArgumentList.Add(eventName);
                using var watcher = Process.Start(start) ?? throw new InvalidOperationException(UiText.T("Не удалось запустить защиту восстановления.", "Could not start cursor recovery."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.T("Не удалось запустить защиту восстановления. ", "Could not start cursor recovery. ") + ex.Message, "CursorPress");
                return 1;
            }
        }
        try
        {
            using (var form = new MainForm(smoke, !args.Contains("--preview"), args.Contains("--startup"))) Application.Run(form);
            clean.Set();
            return 0;
        }
        catch (Exception ex)
        {
            Native.ResetScheme();
            MessageBox.Show(UiText.T("Программа остановлена. ", "Application stopped. ") + ex.Message, "CursorPress");
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static int Watch(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out int pid)) return 1;
        try
        {
            using var clean = EventWaitHandle.OpenExisting(args[2]);
            try { using var parent = Process.GetProcessById(pid); parent.WaitForExit(); }
            catch (ArgumentException) { }
            if (!clean.WaitOne(0)) Native.ResetScheme();
            return 0;
        }
        catch { Native.ResetScheme(); return 1; }
    }

    private static int Shutdown(string[] args)
    {
        try
        {
            int argumentIndex = Array.IndexOf(args, "--shutdown");
            string? targetPath = argumentIndex + 1 < args.Length ? Path.GetFullPath(args[argumentIndex + 1]) : Environment.ProcessPath;
            foreach (var process in Process.GetProcessesByName("CursorPress"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId || !string.Equals(process.MainModule?.FileName, targetPath, StringComparison.OrdinalIgnoreCase)) continue;
                    var threads = new HashSet<uint>();
                    Native.EnumWindows((window, _) =>
                    {
                        uint thread = Native.GetWindowThreadProcessId(window, out uint pid);
                        if (pid == process.Id) threads.Add(thread);
                        return true;
                    }, 0);
                    foreach (uint thread in threads)
                        if (!Native.PostThreadMessage(thread, 0x0012, 0, 0)) return 1;
                    if (threads.Count > 0 && !process.WaitForExit(8000)) return 1;
                }
            }
            return 0;
        }
        catch { return 1; }
    }
}
