// Themes: Dark (Black), White, Glass (Liquid-glass style acrylic), iOS Glass (light frosted acrylic), Midnight.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace NovaBar
{
    public class Theme
    {
        public string Id, Name;
        public bool Dark;          // dark text-on-light vs light-on-dark
        public int Backdrop;       // Native.DWMSBT_* (NONE for solid themes)
        public Color BarBg;        // whole bar fill (alpha < 255 on glass themes; shows the blurred desktop)
        public Color BarBorder;
        public Color Tile;         // widget tile fill
        public Color TileHover;
        public Color TileBorder;
        public Color Text, TextDim, TextFaint;
        public Color Accent, Good, Warn, Bad;
        public Color Track;        // ring/progress track
        public Color PanelBg;      // popup card background (alpha on glass)
        /// <summary>Opaque surface for the shell style (bar, panels and fillets must match exactly).</summary>
        public Color ShellBg
        {
            get
            {
                var p = PanelBg; if (p.A == 255) return p;
                var bse = Dark ? Color.FromArgb(16, 16, 20) : Color.FromArgb(238, 238, 242); double a = p.A / 255.0;
                return Color.FromArgb(255, (int)(p.R * a + bse.R * (1 - a)), (int)(p.G * a + bse.G * (1 - a)), (int)(p.B * a + bse.B * (1 - a)));
            }
        }
        public Color Input;        // input field background (must be OPAQUE: WinForms TextBox can't do alpha)
        public Color[] Series;     // chart palette

        public static Theme Current = Make("dark", "#7C8CFF");
        static readonly string[] ids = { "dark", "white", "glass", "glasslight", "midnight" };
        public static string[] Ids { get { return ids; } }
        public static string NameOf(string id)
        {
            switch (id) { case "white": return "White"; case "glass": return "Liquid Glass"; case "glasslight": return "iOS Glass"; case "midnight": return "Midnight"; default: return "Black"; }
        }

        public static Color Hex(string h, Color def)
        {
            try
            {
                h = (h ?? "").Trim().TrimStart('#');
                if (h.Length == 6) return Color.FromArgb(255, int.Parse(h.Substring(0, 2), NumberStyles.HexNumber), int.Parse(h.Substring(2, 2), NumberStyles.HexNumber), int.Parse(h.Substring(4, 2), NumberStyles.HexNumber));
                if (h.Length == 8) return Color.FromArgb(int.Parse(h.Substring(0, 2), NumberStyles.HexNumber), int.Parse(h.Substring(2, 2), NumberStyles.HexNumber), int.Parse(h.Substring(4, 2), NumberStyles.HexNumber), int.Parse(h.Substring(6, 2), NumberStyles.HexNumber));
            }
            catch { }
            return def;
        }
        public static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        static Color A(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }
        static Color A(int a, Color c) { return Color.FromArgb(a, c.R, c.G, c.B); }

        /// <summary>The bar's own theme: the chosen theme plus the user's bar background colour / opacity (Appearance page).
        /// Extra["barColor"] = "#RRGGBB" ("" = theme colour), Extra["barOpacity"] = 0..100 ("" = theme's own opacity).</summary>
        public static Theme ForBar(AppConfig c)
        {
            var t = Make(c.Theme, c.Accent);
            object v; string hex = c.Extra != null && c.Extra.TryGetValue("barColor", out v) && v != null ? J.AsStr(v) : "";
            int op = -1; if (c.Extra != null && c.Extra.TryGetValue("barOpacity", out v) && v != null) { double d; if (double.TryParse(J.AsStr(v), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) op = (int)Math.Max(0, Math.Min(100, d)); }
            var col = Hex(hex, Color.Empty);
            if (col == Color.Empty && op < 0) return t;
            var baseC = col == Color.Empty ? t.BarBg : col;
            int a = op >= 0 ? (int)Math.Round(op * 2.55) : (col == Color.Empty ? t.BarBg.A : Math.Max(t.BarBg.A, t.IsGlass ? (int)t.BarBg.A : 255));
            t.BarBg = Color.FromArgb(a, baseC.R, baseC.G, baseC.B);
            return t;
        }
        public static Theme Make(string id, string accentHex)
        {
            var accent = Hex(accentHex, Color.FromArgb(124, 140, 255));
            var t = new Theme { Id = id, Name = NameOf(id), Accent = accent };
            switch (id)
            {
                case "white":
                    t.Dark = false; t.Backdrop = Native.DWMSBT_NONE;
                    t.BarBg = A(255, 246, 246, 248); t.BarBorder = A(28, 0, 0, 0);
                    t.Tile = A(255, 255, 255, 255); t.TileHover = A(255, 236, 236, 241); t.TileBorder = A(20, 0, 0, 0);
                    t.Text = A(255, 17, 17, 20); t.TextDim = A(255, 92, 93, 102); t.TextFaint = A(255, 160, 161, 170);
                    t.Track = A(255, 228, 228, 234); t.PanelBg = A(255, 250, 250, 252); t.Input = A(255, 238, 238, 242);
                    t.Good = A(255, 22, 163, 74); t.Warn = A(255, 217, 119, 6); t.Bad = A(255, 220, 38, 38);
                    break;
                case "glass":
                    t.Dark = true; t.Backdrop = Native.DWMSBT_TRANSIENTWINDOW;
                    t.BarBg = A(64, 16, 16, 24); t.BarBorder = A(46, 255, 255, 255);
                    t.Tile = A(30, 255, 255, 255); t.TileHover = A(54, 255, 255, 255); t.TileBorder = A(34, 255, 255, 255);
                    t.Text = A(255, 255, 255, 255); t.TextDim = A(255, 214, 214, 224); t.TextFaint = A(255, 150, 150, 165);
                    t.Track = A(50, 255, 255, 255); t.PanelBg = A(70, 18, 18, 26); t.Input = A(255, 40, 40, 50);
                    t.Good = A(255, 74, 222, 128); t.Warn = A(255, 251, 191, 36); t.Bad = A(255, 248, 113, 113);
                    break;
                case "glasslight":
                    t.Dark = false; t.Backdrop = Native.DWMSBT_TRANSIENTWINDOW;
                    t.BarBg = A(120, 250, 250, 252); t.BarBorder = A(150, 255, 255, 255);
                    t.Tile = A(120, 255, 255, 255); t.TileHover = A(185, 255, 255, 255); t.TileBorder = A(110, 255, 255, 255);
                    t.Text = A(255, 16, 16, 20); t.TextDim = A(255, 70, 72, 82); t.TextFaint = A(255, 125, 127, 138);
                    t.Track = A(70, 0, 0, 0); t.PanelBg = A(140, 250, 250, 252); t.Input = A(255, 245, 245, 248);
                    t.Good = A(255, 22, 163, 74); t.Warn = A(255, 202, 110, 4); t.Bad = A(255, 220, 38, 38);
                    break;
                case "midnight":
                    t.Dark = true; t.Backdrop = Native.DWMSBT_TRANSIENTWINDOW;
                    t.BarBg = A(178, 8, 12, 30); t.BarBorder = A(50, 140, 160, 255);
                    t.Tile = A(34, 120, 140, 255); t.TileHover = A(60, 120, 140, 255); t.TileBorder = A(40, 140, 160, 255);
                    t.Text = A(255, 236, 240, 255); t.TextDim = A(255, 170, 182, 220); t.TextFaint = A(255, 110, 122, 165);
                    t.Track = A(60, 120, 140, 255); t.PanelBg = A(200, 8, 12, 30); t.Input = A(255, 18, 24, 50);
                    t.Good = A(255, 94, 234, 212); t.Warn = A(255, 251, 191, 36); t.Bad = A(255, 251, 113, 133);
                    if (accentHex == null || accentHex.ToUpper() == "#7C8CFF") t.Accent = A(255, 138, 180, 255);
                    break;
                default: // dark / black
                    t.Id = "dark"; t.Name = "Black";
                    t.Dark = true; t.Backdrop = Native.DWMSBT_NONE;
                    t.BarBg = A(255, 12, 12, 15); t.BarBorder = A(26, 255, 255, 255);
                    t.Tile = A(255, 26, 26, 31); t.TileHover = A(255, 37, 37, 44); t.TileBorder = A(16, 255, 255, 255);
                    t.Text = A(255, 242, 242, 245); t.TextDim = A(255, 160, 160, 171); t.TextFaint = A(255, 98, 98, 108);
                    t.Track = A(255, 44, 44, 52); t.PanelBg = A(255, 18, 18, 22); t.Input = A(255, 32, 32, 38);
                    t.Good = A(255, 74, 222, 128); t.Warn = A(255, 251, 191, 36); t.Bad = A(255, 248, 113, 113);
                    break;
            }
            t.Series = new[] { t.Accent, A(255, 56, 189, 248), A(255, 52, 211, 153), A(255, 251, 191, 36), A(255, 244, 114, 182), A(255, 167, 139, 250) };
            return t;
        }

        public bool IsGlass { get { return Backdrop != Native.DWMSBT_NONE; } }

        /// <summary>Accent with a given alpha.</summary>
        public Color AccentA(int a) { return Color.FromArgb(a, Accent); }
        /// <summary>Text color with alpha.</summary>
        public Color TextA(int a) { return Color.FromArgb(a, Text); }
        /// <summary>Pick good/warn/bad from a 0..1 load value.</summary>
        public Color Load(double v) { return v >= 0.85 ? Bad : v >= 0.6 ? Warn : Accent; }
    }
}
