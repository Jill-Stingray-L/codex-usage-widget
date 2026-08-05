using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexUsageWidget.Infrastructure.Windows;

internal static class UsageIconFactory
{
    public static System.Drawing.Icon Create(double? remainingPercent)
    {
        using var bitmap = new System.Drawing.Bitmap(64, 64);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var indicatorColor = remainingPercent switch
        {
            <= 10 => System.Drawing.Color.FromArgb(240, 112, 112),
            <= 25 => System.Drawing.Color.FromArgb(240, 179, 94),
            _ => System.Drawing.Color.FromArgb(101, 216, 146)
        };

        using var backgroundBrush = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(22, 29, 39));
        using var borderPen = new System.Drawing.Pen(indicatorColor, 6f);
        graphics.FillEllipse(backgroundBrush, 3, 3, 58, 58);
        graphics.DrawEllipse(borderPen, 6, 6, 52, 52);

        DrawPercentage(graphics, remainingPercent);
        return CloneIcon(bitmap);
    }

    private static void DrawPercentage(System.Drawing.Graphics graphics, double? remainingPercent)
    {
        var text = remainingPercent is null
            ? "?"
            : Math.Round(Math.Clamp(remainingPercent.Value, 0d, 100d))
                .ToString("0", CultureInfo.InvariantCulture);
        var fontSize = text.Length >= 3 ? 18f : 24f;
        using var font = new System.Drawing.Font(
            "Segoe UI",
            fontSize,
            System.Drawing.FontStyle.Bold,
            System.Drawing.GraphicsUnit.Pixel);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        var textSize = graphics.MeasureString(text, font);
        graphics.DrawString(
            text,
            font,
            textBrush,
            (64f - textSize.Width) / 2f,
            (64f - textSize.Height) / 2f - 1f);
    }

    private static System.Drawing.Icon CloneIcon(System.Drawing.Bitmap bitmap)
    {
        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = System.Drawing.Icon.FromHandle(iconHandle);
            return (System.Drawing.Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
