namespace CursorPress;

internal sealed class Motion
{
    public double Value { get; private set; }
    private double from, target, start, duration;
    public void MoveTo(double next, double now, double milliseconds)
    {
        Update(now);
        from = Value;
        target = next;
        start = now;
        duration = milliseconds;
    }
    public double Update(double now)
    {
        double t = duration <= 0 ? 1 : Math.Clamp((now - start) / duration, 0, 1);
        double ease = t * t * t * (t * (t * 6 - 15) + 10);
        return Value = from + (target - from) * ease;
    }
}

internal sealed class CursorEngine : IDisposable
{
    private const int Steps = 40;
    private readonly Dictionary<uint, nint> originals = new();
    private readonly Dictionary<uint, nint[]> frames = new();
    private readonly Motion motion = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 10 };
    private int lastFrame;
    private bool down, modified, disposed;
    public bool Enabled { get; private set; }
    public bool RightButton { get; set; } = true;
    public bool MiddleButton { get; set; }
    public int PressMs { get; set; } = 100;
    public int ReleaseMs { get; set; } = 170;
    public double Compression { get; private set; } = .18;
    public event Action<Exception>? Failed;

    public CursorEngine()
    {
        timer.Tick += (_, _) =>
        {
            try { Tick(); }
            catch (Exception ex) { SetEnabled(false); Failed?.Invoke(ex); }
        };
    }

    public void SetEnabled(bool enabled)
    {
        if (disposed || Enabled == enabled) return;
        if (enabled)
        {
            Capture();
            try { BuildFrames(); }
            catch { FreeFrames(); FreeOriginals(); throw; }
            down = false;
            lastFrame = 0;
            Enabled = true;
            timer.Start();
        }
        else
        {
            timer.Stop();
            Enabled = false;
            Restore();
            FreeFrames();
            FreeOriginals();
            motion.MoveTo(0, clock.Elapsed.TotalMilliseconds, 0);
        }
    }

    public void ConfigureCompression(double compression)
    {
        bool resume = Enabled;
        SetEnabled(false);
        Compression = Math.Clamp(compression, .05, .4);
        if (resume) SetEnabled(true);
    }

    private void Capture()
    {
        try
        {
            foreach (uint id in Native.CursorIds)
            {
                var shared = Native.LoadCursor(0, (nint)id);
                if (shared == 0) continue;
                var copy = Native.CopyIcon(shared);
                if (copy == 0) throw new System.ComponentModel.Win32Exception();
                originals.Add(id, copy);
            }
            if (originals.Count == 0) throw new InvalidOperationException(UiText.T("Не удалось прочитать системные курсоры.", "Could not read system cursors."));
        }
        catch { FreeOriginals(); throw; }
    }

    private void BuildFrames()
    {
        foreach (var (id, original) in originals)
        {
            var list = new nint[Steps + 1];
            frames.Add(id, list);
            for (int i = 0; i <= Steps; i++) list[i] = Native.Scale(original, 1 - Compression * i / Steps);
        }
    }

    private void Tick()
    {
        bool pressed = Native.ButtonDown(1) || (RightButton && Native.ButtonDown(2)) || (MiddleButton && Native.ButtonDown(4));
        double now = clock.Elapsed.TotalMilliseconds;
        if (pressed != down)
        {
            down = pressed;
            motion.MoveTo(pressed ? 1 : 0, now, pressed ? PressMs : ReleaseMs);
        }
        int frame = (int)Math.Round(motion.Update(now) * Steps);
        if (frame == lastFrame) return;
        if (frame == 0) Restore();
        else
        {
            modified = true;
            foreach (var (id, list) in frames) Apply(list[frame], id);
        }
        lastFrame = frame;
    }

    private static void Apply(nint source, uint id)
    {
        var copy = Native.CopyIcon(source);
        if (copy == 0) throw new System.ComponentModel.Win32Exception();
        if (!Native.SetSystemCursor(copy, id))
        {
            Native.DestroyCursor(copy);
            throw new System.ComponentModel.Win32Exception();
        }
    }

    private void Restore()
    {
        if (!modified) return;
        bool success = true;
        foreach (var (id, cursor) in originals)
        {
            try { Apply(cursor, id); }
            catch { success = false; }
        }
        if (!success && !Native.ResetScheme())
            throw new InvalidOperationException(UiText.T("Не удалось восстановить курсоры. Запустите Restore-Cursors.cmd.", "Could not restore cursors. Run Restore-Cursors.cmd."));
        modified = false;
        lastFrame = 0;
    }
    private void FreeFrames()
    {
        foreach (var list in frames.Values) foreach (var handle in list) if (handle != 0) Native.DestroyCursor(handle);
        frames.Clear();
    }
    private void FreeOriginals()
    {
        foreach (var handle in originals.Values) Native.DestroyCursor(handle);
        originals.Clear();
    }
    public void Dispose()
    {
        if (disposed) return;
        SetEnabled(false);
        timer.Dispose();
        disposed = true;
    }
}
