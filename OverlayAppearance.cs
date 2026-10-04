using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace MonitorGate
{
    // The real badge and the settings preview share font resolution, measurement,
    // and painting. All font sizes are stored in points and drawn in pixels.
    internal static class OverlayAppearance
    {
        internal const string Message = "Pointer movement enabled";

        internal static string[] AvailableFontNames()
        {
            SortedSet<string> names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using (InstalledFontCollection installed = new InstalledFontCollection())
                foreach (FontFamily family in installed.Families)
                    using (family)
                        if (family.IsStyleAvailable(FontStyle.Regular) || family.IsStyleAvailable(FontStyle.Bold))
                            names.Add(family.Name);
            string[] result = new string[names.Count];
            names.CopyTo(result);
            return result;
        }

        internal static string ResolveFontName(string requested)
        {
            string resolved = SupportedFamilyName(requested);
            if (resolved != null) return resolved;
            resolved = SupportedFamilyName("Segoe UI");
            if (resolved != null) return resolved;
            using (FontFamily generic = FontFamily.GenericSansSerif) return generic.Name;
        }

        private static string SupportedFamilyName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            try
            {
                using (FontFamily family = new FontFamily(name))
                    return family.IsStyleAvailable(FontStyle.Regular) || family.IsStyleAvailable(FontStyle.Bold) ? family.Name : null;
            }
            catch (ArgumentException) { return null; }
        }

        internal static bool SupportsBold(string name)
        {
            try
            {
                using (FontFamily family = new FontFamily(ResolveFontName(name)))
                    return family.IsStyleAvailable(FontStyle.Bold);
            }
            catch (ArgumentException)
            {
                using (FontFamily generic = FontFamily.GenericSansSerif) return generic.IsStyleAvailable(FontStyle.Bold);
            }
        }

        internal static Font CreateFont(UserPreferences preferences, float scale)
        {
            if (preferences == null) throw new ArgumentNullException("preferences");
            ValidateScale(scale);
            if (Single.IsNaN(preferences.OverlayFontSize) || Single.IsInfinity(preferences.OverlayFontSize) ||
                preferences.OverlayFontSize < 6 || preferences.OverlayFontSize > 48)
                throw new ArgumentException("글자 크기는 6~48pt 범위여야 합니다.");
            float pixels = preferences.OverlayFontSize * (96f / 72f) * scale;
            string resolved = ResolveFontName(preferences.OverlayFontName);
            try { return FontForFamily(resolved, pixels, preferences.OverlayFontBold); }
            catch (ArgumentException)
            {
                // A font can be uninstalled after enumeration. Resolve a supported
                // sans-serif font again instead of failing to paint the badge.
                string fallback = ResolveFontName(null);
                try { return FontForFamily(fallback, pixels, preferences.OverlayFontBold); }
                catch (ArgumentException)
                {
                    using (FontFamily generic = FontFamily.GenericSansSerif)
                        return FontForFamily(generic.Name, pixels, preferences.OverlayFontBold);
                }
            }
        }

        private static Font FontForFamily(string name, float pixels, bool bold)
        {
            using (FontFamily family = new FontFamily(name))
            {
                FontStyle style = SelectFontStyle(bold, family.IsStyleAvailable(FontStyle.Regular), family.IsStyleAvailable(FontStyle.Bold));
                return new Font(family, pixels, style, GraphicsUnit.Pixel);
            }
        }

        internal static FontStyle SelectFontStyle(bool preferBold, bool regularAvailable, bool boldAvailable)
        {
            if (preferBold && boldAvailable) return FontStyle.Bold;
            if (regularAvailable) return FontStyle.Regular;
            if (boldAvailable) return FontStyle.Bold;
            throw new ArgumentException("이 글꼴에는 사용할 수 있는 보통 또는 굵은 스타일이 없습니다.");
        }

        internal static Size MeasureBox(UserPreferences preferences, float scale)
        {
            using (Font font = CreateFont(preferences, scale))
            using (Bitmap surface = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(surface))
            using (StringFormat format = TextFormat())
            {
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                SizeF text = graphics.MeasureString(Message, font, PointF.Empty, format);
                if (preferences.OverlayAutoSize)
                    return new Size((int)Math.Ceiling(text.Width + 64 * scale), (int)Math.Ceiling(text.Height + 12 * scale));
                return new Size((int)Math.Ceiling(Math.Max(preferences.OverlayWidth * scale, text.Width + 16 * scale)),
                    (int)Math.Ceiling(Math.Max(preferences.OverlayHeight * scale, text.Height + 8 * scale)));
            }
        }

        internal static void DrawText(Graphics graphics, Rectangle bounds, UserPreferences preferences, float scale)
        {
            if (graphics == null) throw new ArgumentNullException("graphics");
            using (Font font = CreateFont(preferences, scale))
            using (Brush brush = new SolidBrush(preferences.OverlayForeground))
            using (StringFormat format = TextFormat())
            {
                TextRenderingHint previous = graphics.TextRenderingHint;
                try
                {
                    graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    graphics.DrawString(Message, font, brush, bounds, format);
                }
                finally { graphics.TextRenderingHint = previous; }
            }
        }

        internal static Size FitToMonitor(Size desired, Rectangle bounds, float scale)
        {
            ValidateScale(scale);
            int width = Math.Max(1, Math.Min(desired.Width, bounds.Width));
            double availableHeight = Math.Max(1d, (double)bounds.Height - Math.Ceiling(16d * scale));
            int height = Math.Max(1, (int)Math.Min(desired.Height, availableHeight));
            return new Size(width, height);
        }

        private static StringFormat TextFormat()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags |= StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.EllipsisCharacter;
            return format;
        }

        private static void ValidateScale(float scale)
        {
            if (Single.IsNaN(scale) || Single.IsInfinity(scale) || scale <= 0)
                throw new ArgumentOutOfRangeException("scale");
        }
    }
}
