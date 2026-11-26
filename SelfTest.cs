using System.Text;

namespace CursorPress;

internal static class SelfTest
{
    internal static int Run()
    {
        var log = new StringBuilder();
        try
        {
            void Check(bool condition, string description)
            {
                if (!condition) throw new Exception(description);
                log.AppendLine("PASS " + description);
            }
            var motion = new Motion();
            motion.MoveTo(1, 0, 100);
            Check(motion.Update(0) == 0, "Animation starts at original size");
            double previous = 0;
            for (int t = 1; t <= 100; t++)
            {
                double current = motion.Update(t);
                Check(current >= previous && current <= 1, $"Bounded monotonic press at {t} ms");
                previous = current;
            }
            Check(motion.Update(100) == 1, "Press reaches target");
            motion.MoveTo(0, 100, 170);
            Check(motion.Update(270) == 0, "Release restores exact original");
            motion.MoveTo(1, 300, 100);
            double interrupted = motion.Update(340);
            motion.MoveTo(0, 340, 170);
            Check(Math.Abs(motion.Update(340) - interrupted) < 1e-10, "Early release has no position jump");
            Check(motion.Update(510) == 0, "Early release settles");
            motion.MoveTo(1, 600, 0);
            Check(motion.Update(600) == 1, "Instant transition");
            foreach (uint id in Native.CursorIds)
            {
                nint original = Native.LoadCursor(0, (nint)id);
                if (original == 0) continue;
                var source = Native.Describe(original);
                foreach (double scale in new[] { 1.0, .82, .60 })
                {
                    nint copy = Native.Scale(original, scale);
                    try
                    {
                        var actual = Native.Describe(copy);
                        bool monochrome = Native.IsMonochrome(original);
                        Check(actual.Width == (monochrome ? Math.Max(1, (int)Math.Round(source.Width * scale)) : source.Width), $"Cursor {id} canvas width at {scale}");
                        Check(actual.Height == (monochrome ? Math.Max(1, (int)Math.Round(source.Height * scale)) : source.Height), $"Cursor {id} canvas height at {scale}");
                        Check(actual.HotX < actual.Width && actual.HotY < actual.Height, $"Cursor {id} hotspot in bounds at {scale}");
                        if (!monochrome)
                        {
                            Check(actual.HotX == source.HotX && actual.HotY == source.HotY, $"Cursor {id} fixed hotspot at {scale}");
                            using var frame = Native.RenderSmooth(original, scale);
                            bool visible = false;
                            for (int y = 0; y < frame.Height; y++)
                                for (int x = 0; x < frame.Width; x++) visible |= frame.GetPixel(x, y).A != 0;
                            Check(visible, $"Cursor {id} visible RGBA artwork at {scale}");
                        }
                    }
                    finally { Native.DestroyCursor(copy); }
                }
            }
            using (var diagonal = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(diagonal))
                {
                    g.Clear(Color.Transparent);
                    g.FillPolygon(Brushes.White, new Point[] { new(3, 2), new(3, 28), new(24, 23) });
                }
                nint sourceIcon = diagonal.GetHicon();
                try
                {
                    nint smooth = Native.Scale(sourceIcon, .813);
                    try
                    {
                        using var icon = Icon.FromHandle(smooth);
                        using var actual = icon.ToBitmap();
                        int partialAlpha = 0;
                        for (int y = 0; y < actual.Height; y++)
                            for (int x = 0; x < actual.Width; x++)
                            { int alpha = actual.GetPixel(x, y).A; if (alpha > 0 && alpha < 255) partialAlpha++; }
                        Check(partialAlpha > 12, "Smoothed native cursor retains partially transparent edge pixels");
                    }
                    finally { Native.DestroyCursor(smooth); }
                }
                finally { Native.DestroyIcon(sourceIcon); }
            }
            using (var centered = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(centered))
                { g.Clear(Color.Transparent); g.FillRectangle(Brushes.White, 6, 8, 7, 7); }
                nint iconHandle = centered.GetHicon();
                nint cursor = 0;
                try
                {
                    if (!Native.GetIconInfo(iconHandle, out var info)) throw new Exception("Cannot read test icon");
                    try
                    {
                        info.IsIcon = false;
                        info.HotX = 9;
                        info.HotY = 11;
                        cursor = Native.CreateIconIndirect(ref info);
                    }
                    finally { Native.DeleteObject(info.Mask); Native.DeleteObject(info.Color); }
                    Check(cursor != 0, "Nonzero-hotspot regression cursor created");
                    for (int step = 0; step <= 40; step++)
                    {
                        using var frame = Native.RenderSmooth(cursor, 1 - .4 * step / 40);
                        double total = 0, cx = 0, cy = 0;
                        for (int y = 0; y < frame.Height; y++)
                            for (int x = 0; x < frame.Width; x++)
                            { double a = frame.GetPixel(x, y).A; total += a; cx += a * x; cy += a * y; }
                        Check(total > 0 && Math.Abs(cx / total - 9) < .001 && Math.Abs(cy / total - 11) < .001,
                            $"No horizontal/vertical drift at fractional frame {step}");
                    }
                }
                finally { if (cursor != 0) Native.DestroyCursor(cursor); Native.DestroyIcon(iconHandle); }
            }
            string originalLanguage = UiText.Language;
            UiText.Language = "ru";
            Check(UiText.T("русский", "English") == "русский", "Russian translation selected");
            UiText.Language = "en";
            Check(UiText.T("русский", "English") == "English", "English translation selected");
            UiText.Language = originalLanguage;
            using (var engine = new CursorEngine()) { engine.SetEnabled(true); engine.SetEnabled(false); }
            log.AppendLine("PASS Full frame cache allocation and cleanup");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.log"), log.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            log.AppendLine("FAIL " + ex);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.log"), log.ToString());
            return 1;
        }
    }
}
