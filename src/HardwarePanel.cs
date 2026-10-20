using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MiniCockpitLab
{
    internal sealed class HardwareControl
    {
        public string Id, Label;
        public int Panel;
        public RectangleF Bounds;
        public string[] Codes;
        public bool Button;
    }

    internal sealed class HardwarePanel : UserControl
    {
        private static readonly Color Mint = Color.FromArgb(97, 226, 180);
        private static readonly Color White = Color.FromArgb(235, 242, 251);
        private static readonly Color Muted = Color.FromArgb(143, 160, 181);
        private readonly Image[] photographs = new Image[3];
        private readonly List<HardwareControl> controls = CreateControls();
        private readonly Dictionary<string, DateTime> pulses = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, RectangleF> hitAreas = new Dictionary<string, RectangleF>();
        private readonly System.Windows.Forms.Timer pulseTimer = new System.Windows.Forms.Timer { Interval = 50 };
        private readonly ToolTip tooltip = new ToolTip();
        private SimulatorState simulator;
        private string lastInput = "Touch a physical control to see it light up here.";
        private string hovered;
        public string LastControlId { get; private set; }

        public HardwarePanel(string assetFolder)
        {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(12, 18, 28);
            AccessibleName = "Live miniCOCKPIT hardware";
            string[] files = { "minifcu.png", "miniefis.png", "miniovhd.png" };
            for (int i = 0; i < files.Length; i++)
                using (Image image = Image.FromFile(Path.Combine(assetFolder, files[i])))
                    photographs[i] = new Bitmap(image);
            pulseTimer.Tick += delegate
            {
                if (pulses.Count == 0) return;
                var expired = new List<string>();
                foreach (var pulse in pulses)
                    if (pulse.Value <= DateTime.UtcNow) expired.Add(pulse.Key);
                foreach (string id in expired) pulses.Remove(id);
                if (Visible) Invalidate();
            };
            pulseTimer.Start();
        }

        public void Observe(InputFrame frame, string description)
        {
            if (frame.Code == "901" || frame.Code == "956" || frame.Code == "957"
                || frame.Code == "958" || frame.Code == "959") return;
            HardwareControl control = controls.Find(c => Array.IndexOf(c.Codes, frame.Code) >= 0);
            LastControlId = control == null ? null : control.Id;
            if (control != null)
            {
                bool released = frame.Code == "575" || frame.Code == "577" || frame.Code == "578";
                if (released) pulses.Remove(control.Id);
                else pulses[control.Id] = DateTime.UtcNow.AddSeconds(2);
            }
            lastInput = (control == null ? "Unplaced input" : control.Label) + "  /  " + frame.Raw
                + (String.IsNullOrWhiteSpace(description) ? "" : "  /  " + description);
            AccessibleDescription = lastInput;
            Invalidate();
        }

        public void SetSimulatorState(SimulatorState state)
        {
            if (Object.ReferenceEquals(simulator, state)) return;
            simulator = state;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            DrawText(g, "Your hardware, live.", 24, FontStyle.Bold, White, new RectangleF(26, 14, width - 52, 43));
            DrawText(g, "Physical inputs glow on the exact control. AP1 / AP2 indicators follow MSFS.", 10, FontStyle.Regular, Muted,
                new RectangleF(28, 59, width - 56, 25));
            int sideWidth = (width - 76) / 4;
            int centerWidth = width - 76 - sideWidth * 2;
            int centerLeft = 24 + sideWidth + 14;
            int rightLeft = centerLeft + centerWidth + 14;
            int cardHeight = Math.Max(420, height - 157);
            DrawDevice(g, new Rectangle(24, 99, sideWidth, cardHeight), 1, "miniEFIS", "FLIGHT INSTRUMENTS");
            DrawDevice(g, new Rectangle(centerLeft, 99, centerWidth, cardHeight), 0, "miniFCU", "FLIGHT CONTROL UNIT");
            DrawDevice(g, new Rectangle(rightLeft, 99, sideWidth, cardHeight), 2,
                "miniOVHD", "OVERHEAD");
            DrawText(g, lastInput, 10, FontStyle.Bold, Mint, new RectangleF(28, height - 45, width - 56, 32));
        }

        private void DrawDevice(Graphics g, Rectangle bounds, int panel, string title, string subtitle)
        {
            using (var fill = new SolidBrush(Color.FromArgb(20, 29, 42)))
            using (var outline = new Pen(Color.FromArgb(43, 57, 74)))
            using (var path = Round(bounds, 12))
            {
                g.FillPath(fill, path);
                g.DrawPath(outline, path);
            }
            DrawText(g, title, 15, FontStyle.Bold, White, new RectangleF(bounds.X + 20, bounds.Y + 10, bounds.Width - 40, 31));
            DrawText(g, subtitle, 8, FontStyle.Bold, Muted, new RectangleF(bounds.X + 20, bounds.Y + 42, bounds.Width - 40, 19));
            int footer = 130;
            var available = new RectangleF(bounds.X + 16, bounds.Y + 68, bounds.Width - 32, bounds.Height - 68 - footer);
            Image image = photographs[panel];
            float scale = Math.Min(available.Width / image.Width, available.Height / image.Height);
            var picture = new RectangleF(available.X + (available.Width - image.Width * scale) / 2,
                available.Y + (available.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
            g.DrawImage(image, picture);
            if (panel == 0)
            {
                DrawDisplay(g, picture, new Rectangle(247, 584, 126, 45),
                    simulator == null || !simulator.FlightLevelChange ? "---" : Math.Round(simulator.Speed).ToString("000", CultureInfo.InvariantCulture));
                DrawDisplay(g, picture, new Rectangle(416, 581, 145, 53),
                    simulator == null ? "---" : Math.Round(simulator.Heading).ToString("000", CultureInfo.InvariantCulture));
                DrawDisplay(g, picture, new Rectangle(809, 583, 168, 46),
                    simulator == null ? "-----" : Math.Round(simulator.Altitude).ToString("00000", CultureInfo.InvariantCulture));
                DrawDisplay(g, picture, new Rectangle(1009, 581, 152, 53),
                    simulator == null ? "-----" : Math.Round(simulator.VerticalSpeed).ToString("+0000;-0000;0000", CultureInfo.InvariantCulture));
                DrawModeDot(g, picture, 365, 609, simulator != null && simulator.FlightLevelChange);
                DrawModeDot(g, picture, 547, 609, simulator != null && simulator.HeadingHold);
                DrawModeDot(g, picture, 990, 609, simulator != null && simulator.AltitudeHold);
            }
            foreach (HardwareControl control in controls)
            {
                if (control.Panel != panel) continue;
                var target = new RectangleF(picture.X + control.Bounds.X * picture.Width, picture.Y + control.Bounds.Y * picture.Height,
                    control.Bounds.Width * picture.Width, control.Bounds.Height * picture.Height);
                hitAreas[control.Id] = target;
                DateTime expires;
                bool active = pulses.TryGetValue(control.Id, out expires) && expires > DateTime.UtcNow;
                bool simulatorOn = simulator != null && simulator.IsCaravan
                    && ((control.Id == "ap1" && simulator.Autopilot) || (control.Id == "ap2" && simulator.NavigationHold));
                if (control.Button)
                {
                    using (var shade = new SolidBrush(Color.FromArgb(9, 13, 17)))
                    {
                        g.FillRectangle(shade, target.X + target.Width * .18f, target.Y + target.Height * .12f,
                            target.Width * .64f, target.Height * .34f);
                        if (panel == 2)
                            g.FillRectangle(shade, target.X + target.Width * .18f, target.Y + target.Height * .62f,
                                target.Width * .64f, target.Height * .25f);
                    }
                    if (simulatorOn)
                        using (var led = new SolidBrush(Mint))
                            g.FillRectangle(led, target.X + target.Width * .28f, target.Y + target.Height * .18f, target.Width * .44f, 3);
                }
                if (active)
                {
                    using (var glow = new Pen(Color.FromArgb(65, Mint), 10))
                    using (var border = new Pen(Mint, 2.5f))
                    using (var wash = new SolidBrush(Color.FromArgb(45, Mint)))
                    {
                        g.FillRectangle(wash, target);
                        g.DrawRectangle(glow, target.X, target.Y, target.Width, target.Height);
                        g.DrawRectangle(border, target.X, target.Y, target.Width, target.Height);
                    }
                }
            }
            if (panel == 0)
            {
                int y = bounds.Bottom - 112;
                DrawText(g, "CARAVAN PROFILE", 8, FontStyle.Bold, Muted, new RectangleF(bounds.X + 22, y, bounds.Width - 44, 20));
                string modes = simulator == null ? "Connect MSFS from Start for live mode indicators."
                    : "AP " + (simulator.Autopilot ? "ON" : "OFF") + "   NAV " + (simulator.NavigationHold ? "ON" : "--")
                        + "   HDG " + (simulator.HeadingHold ? "ON" : "--");
                DrawText(g, modes, 10, FontStyle.Bold, simulator == null ? Muted : Mint, new RectangleF(bounds.X + 22, y + 21, bounds.Width - 44, 24));
                if (simulator != null)
                    DrawText(g, "ALT " + (simulator.AltitudeHold ? "HOLD" : "--") + "   FLC " + (simulator.FlightLevelChange ? "ON" : "--")
                        + "   VS " + (simulator.VerticalHold ? "ON" : "--"), 10, FontStyle.Bold, Mint,
                        new RectangleF(bounds.X + 22, y + 45, bounds.Width - 44, 24));
                DrawText(g, "Push / pull: HDG = heading / ALT = hold / SPD = FLC",
                    9, FontStyle.Regular, Muted, new RectangleF(bounds.X + 22, y + 77, bounds.Width - 44, 22));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string id = null;
            foreach (var area in hitAreas)
                if (area.Value.Contains(e.Location)) { id = area.Key; break; }
            if (id == hovered) return;
            hovered = id;
            HardwareControl control = id == null ? null : controls.Find(c => c.Id == id);
            tooltip.SetToolTip(this, control == null ? "" : control.Label + " / input " + String.Join(", ", control.Codes));
        }

        private static void DrawDisplay(Graphics g, RectangleF picture, Rectangle source, string value)
        {
            var target = new RectangleF(picture.X + (source.X - 140) * picture.Width / 1125,
                picture.Y + (source.Y - 425) * picture.Height / 500,
                source.Width * picture.Width / 1125, source.Height * picture.Height / 500);
            using (var background = new SolidBrush(Color.FromArgb(6, 10, 9)))
                g.FillRectangle(background, target);
            using (var font = new Font("Consolas", Math.Max(8, target.Height * .94f), FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, value, font, Rectangle.Round(target), Color.FromArgb(246, 212, 126),
                    TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static void DrawModeDot(Graphics g, RectangleF picture, int x, int y, bool on)
        {
            float scale = picture.Width / 1125;
            float cx = picture.X + (x - 140) * scale;
            float cy = picture.Y + (y - 425) * scale;
            using (var background = new SolidBrush(Color.FromArgb(6, 10, 9)))
                g.FillRectangle(background, cx - 14 * scale, cy - 14 * scale, 28 * scale, 28 * scale);
            if (on)
                using (var indicator = new SolidBrush(Color.FromArgb(246, 212, 126)))
                    g.FillEllipse(indicator, cx - 8 * scale, cy - 8 * scale, 16 * scale, 16 * scale);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawText(Graphics g, string text, float size, FontStyle style, Color color, RectangleF bounds)
        {
            using (var font = new Font("Segoe UI", size, style))
                TextRenderer.DrawText(g, text, font, Rectangle.Round(bounds), color,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static List<HardwareControl> CreateControls()
        {
            var result = new List<HardwareControl>();
            Rectangle[] crops = { new Rectangle(140, 425, 1125, 500), new Rectangle(125, 245, 755, 435), new Rectangle(145, 230, 910, 510) };
            Action<int, string, string, Rectangle, bool, string> add = (panel, id, label, source, button, codes) =>
            {
                Rectangle crop = crops[panel];
                result.Add(new HardwareControl { Id = id, Label = label, Panel = panel, Button = button,
                    Bounds = new RectangleF((source.X - crop.X) / (float)crop.Width, (source.Y - crop.Y) / (float)crop.Height,
                        source.Width / (float)crop.Width, source.Height / (float)crop.Height),
                    Codes = codes.Split(',') });
            };
            add(0, "speed", "SPD knob", new Rectangle(231, 695, 130, 133), false, "11,12,13,14");
            add(0, "heading", "HDG knob", new Rectangle(424, 694, 129, 133), false, "1,2,3,4");
            add(0, "altitude", "ALT knob / ALT HOLD", new Rectangle(838, 690, 126, 140), false, "15,16,17,18");
            add(0, "verticalspeed", "V/S knob", new Rectangle(1052, 691, 131, 135), false, "19,20,21,22");
            add(0, "spdmach", "SPD / MACH", new Rectangle(159, 657, 64, 67), false, "56");
            add(0, "hdgtrk", "HDG / TRK", new Rectangle(669, 653, 65, 68), false, "57");
            add(0, "metricalt", "METRIC ALT", new Rectangle(977, 654, 67, 69), false, "58");
            add(0, "ap1", "AP1 / Caravan autopilot", new Rectangle(613, 729, 78, 76), true, "50");
            add(0, "ap2", "AP2 / Caravan NAV", new Rectangle(711, 729, 77, 76), true, "51");
            add(0, "athr", "A/THR", new Rectangle(662, 818, 77, 76), true, "52");
            add(0, "loc", "LOC", new Rectangle(451, 843, 76, 64), true, "53");
            add(0, "exped", "EXPED", new Rectangle(862, 843, 77, 64), true, "");
            add(0, "appr", "APPR", new Rectangle(1082, 843, 77, 64), true, "55");
            add(1, "chrono", "CHRONO", new Rectangle(138, 478, 83, 80), false, "61");
            add(1, "qnh", "BARO / QNH", new Rectangle(300, 475, 126, 122), false, "69,70,101,102,103,104");
            add(1, "fd", "FD", new Rectangle(291, 600, 69, 58), true, "62");
            add(1, "ls", "LS", new Rectangle(369, 600, 69, 58), true, "63");
            string[] efisButtons = { "cstr", "wpt", "vord", "ndb", "arpt" };
            for (int i = 0; i < efisButtons.Length; i++)
                add(1, efisButtons[i], efisButtons[i].ToUpperInvariant(), new Rectangle(461 + i * 81, 351, 74, 60), true,
                    (64 + i).ToString(CultureInfo.InvariantCulture));
            add(1, "ndmode", "ND mode selector", new Rectangle(500, 451, 125, 133), false, "71,72,73,74,75,76");
            add(1, "ndrange", "ND range selector", new Rectangle(706, 451, 126, 133), false, "80,81,82,83,84,85");
            add(2, "flap3", "LDG FLAP 3", new Rectangle(187, 310, 108, 112), true, "200");
            add(2, "wingai", "WING ANTI ICE", new Rectangle(343, 310, 111, 112), true, "201");
            add(2, "engai", "ENGINE ANTI ICE", new Rectangle(483, 310, 110, 112), true, "202");
            add(2, "winheat", "PROBE / WINDOW HEAT", new Rectangle(619, 310, 109, 112), true, "203");
            add(2, "apubleed", "APU BLEED", new Rectangle(778, 338, 108, 107), true, "204");
            add(2, "extpwr", "EXTERNAL POWER", new Rectangle(916, 310, 111, 112), true, "205,575");
            add(2, "apumaster", "APU MASTER", new Rectangle(778, 468, 108, 107), true, "206");
            add(2, "apustart", "APU START", new Rectangle(778, 596, 108, 107), true, "207,577");
            add(2, "call", "CALL", new Rectangle(192, 449, 100, 91), false, "208,578");
            add(2, "wiper", "WIPER", new Rectangle(168, 560, 147, 171), false, "231,232,233");
            add(2, "strobe", "STROBE", new Rectangle(346, 460, 100, 99), false, "212,213,214");
            add(2, "beacon", "BEACON", new Rectangle(483, 460, 100, 99), false, "215,216");
            add(2, "navigation", "NAV & LOGO", new Rectangle(625, 460, 100, 99), false, "217,218,219");
            add(2, "runway", "RWY TURN OFF / Caravan taxi", new Rectangle(347, 601, 101, 98), false, "223,224");
            add(2, "landing", "LANDING", new Rectangle(481, 602, 103, 97), false, "225,226,227");
            add(2, "nose", "NOSE / Caravan taxi", new Rectangle(624, 602, 101, 97), false, "228,229,230");
            add(2, "dome", "DOME", new Rectangle(920, 462, 100, 98), false, "220,221,222");
            add(2, "seatbelts", "SEAT BELTS", new Rectangle(920, 603, 101, 98), false, "210,211");
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pulseTimer.Dispose();
                tooltip.Dispose();
                foreach (Image image in photographs) if (image != null) image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
