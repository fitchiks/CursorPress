using System.Runtime.InteropServices;
using System.Drawing.Imaging;

namespace CursorPress;

internal static class Native
{
    internal static readonly uint[] CursorIds = [32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651];
    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotX, HotY;
        public nint Mask, Color;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public nint Bits;
    }
    [DllImport("user32.dll")] internal static extern nint LoadCursor(nint instance, nint name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint CopyImage(nint image, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll")] internal static extern bool DestroyCursor(nint cursor);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern int GetObject(nint obj, int size, out BitmapInfo bitmap);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SystemParametersInfo(uint action, uint param, nint value, uint flags);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);
    internal delegate bool EnumWindowsCallback(nint window, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsCallback callback, nint data);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    internal static bool ResetScheme() => SystemParametersInfo(0x0057, 0, 0, 0);
    internal static bool ButtonDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    internal static (int Width, int Height, uint HotX, uint HotY) Describe(nint cursor)
    {
        if (!GetIconInfo(cursor, out var info)) throw new System.ComponentModel.Win32Exception();
        try
        {
            var bitmap = info.Color != 0 ? info.Color : info.Mask;
            if (GetObject(bitmap, Marshal.SizeOf<BitmapInfo>(), out var data) == 0)
                throw new System.ComponentModel.Win32Exception();
            return (data.Width, info.Color == 0 ? data.Height / 2 : data.Height, info.HotX, info.HotY);
        }
        finally { DeleteObject(info.Mask); if (info.Color != 0) DeleteObject(info.Color); }
    }

    internal static bool IsMonochrome(nint cursor)
    {
        if (!GetIconInfo(cursor, out var info)) throw new System.ComponentModel.Win32Exception();
        try { return info.Color == 0; }
        finally { DeleteObject(info.Mask); if (info.Color != 0) DeleteObject(info.Color); }
    }

    internal static nint Scale(nint original, double scale)
    {
        if (Math.Abs(scale - 1) < 1e-8)
        {
            var copy = CopyIcon(original);
            if (copy == 0) throw new System.ComponentModel.Win32Exception();
            return copy;
        }
        if (IsMonochrome(original)) return ScaleLegacy(original, scale);
        var size = Describe(original);
        using var scaled = RenderSmooth(original, scale);
        nint icon = scaled.GetHicon();
        try
        {
            if (!GetIconInfo(icon, out var info)) throw new System.ComponentModel.Win32Exception();
            try
            {
                info.IsIcon = false;
                info.HotX = size.HotX;
                info.HotY = size.HotY;
                nint result = CreateIconIndirect(ref info);
                if (result == 0) throw new System.ComponentModel.Win32Exception();
                return result;
            }
            finally { DeleteObject(info.Mask); if (info.Color != 0) DeleteObject(info.Color); }
        }
        finally { DestroyIcon(icon); }
    }

    internal static Bitmap RenderSmooth(nint original, double scale)
    {
        var size = Describe(original);
        using var icon = Icon.FromHandle(original);
        using var source = icon.ToBitmap();
        var sourceBytes = new byte[source.Width * source.Height * 4];
        var sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < source.Height; row++)
                Marshal.Copy(sourceData.Scan0 + row * sourceData.Stride, sourceBytes, row * source.Width * 4, source.Width * 4);
        }
        finally { source.UnlockBits(sourceData); }
        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var resultBytes = new byte[size.Width * size.Height * 4];
        try
        {
            double half = .5 / scale;
            double normalization = scale * scale;
            for (int y = 0; y < result.Height; y++)
            {
                double sy = size.HotY + (y - (double)size.HotY) / scale;
                double top = sy - half, bottom = sy + half;
                int y0 = Math.Max(0, (int)Math.Floor(top + .5));
                int y1 = Math.Min(source.Height - 1, (int)Math.Ceiling(bottom + .5) - 1);
                for (int x = 0; x < result.Width; x++)
                {
                    double sx = size.HotX + (x - (double)size.HotX) / scale;
                    double left = sx - half, right = sx + half;
                    int x0 = Math.Max(0, (int)Math.Floor(left + .5));
                    int x1 = Math.Min(source.Width - 1, (int)Math.Ceiling(right + .5) - 1);
                    double alpha = 0, red = 0, green = 0, blue = 0;
                    for (int iy = y0; iy <= y1; iy++)
                    {
                        double wy = Math.Max(0, Math.Min(bottom, iy + .5) - Math.Max(top, iy - .5));
                        for (int ix = x0; ix <= x1; ix++)
                        {
                            double wx = Math.Max(0, Math.Min(right, ix + .5) - Math.Max(left, ix - .5));
                            int offset = (iy * source.Width + ix) * 4;
                            double weight = wx * wy * sourceBytes[offset + 3];
                            alpha += weight;
                            red += weight * sourceBytes[offset + 2];
                            green += weight * sourceBytes[offset + 1];
                            blue += weight * sourceBytes[offset];
                        }
                    }
                    int a = Math.Clamp((int)Math.Round(alpha * normalization), 0, 255);
                    if (a > 0 && alpha > 0)
                    {
                        int offset = (y * size.Width + x) * 4;
                        resultBytes[offset] = (byte)Math.Clamp((int)Math.Round(blue / alpha), 0, 255);
                        resultBytes[offset + 1] = (byte)Math.Clamp((int)Math.Round(green / alpha), 0, 255);
                        resultBytes[offset + 2] = (byte)Math.Clamp((int)Math.Round(red / alpha), 0, 255);
                        resultBytes[offset + 3] = (byte)a;
                    }
                }
            }
            var resultData = result.LockBits(new Rectangle(0, 0, size.Width, size.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < size.Height; row++)
                    Marshal.Copy(resultBytes, row * size.Width * 4, resultData.Scan0 + row * resultData.Stride, size.Width * 4);
            }
            finally { result.UnlockBits(resultData); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static nint ScaleLegacy(nint original, double scale)
    {
        var size = Describe(original);
        int width = Math.Max(1, (int)Math.Round(size.Width * scale));
        int height = Math.Max(1, (int)Math.Round(size.Height * scale));
        var resized = CopyImage(original, 2, width, height, 0);
        if (resized == 0) throw new System.ComponentModel.Win32Exception();
        try
        {
            if (!GetIconInfo(resized, out var info)) throw new System.ComponentModel.Win32Exception();
            try
            {
                info.IsIcon = false;
                info.HotX = (uint)Math.Clamp((int)Math.Round(size.HotX * width / (double)size.Width), 0, width - 1);
                info.HotY = (uint)Math.Clamp((int)Math.Round(size.HotY * height / (double)size.Height), 0, height - 1);
                var result = CreateIconIndirect(ref info);
                if (result == 0) throw new System.ComponentModel.Win32Exception();
                return result;
            }
            finally { DeleteObject(info.Mask); if (info.Color != 0) DeleteObject(info.Color); }
        }
        finally { DestroyCursor(resized); }
    }
}
