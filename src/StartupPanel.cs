using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniCockpitLab
{
    internal sealed class StartupPanel : UserControl
    {
        private static readonly Color Background = Color.FromArgb(12, 18, 28);
        private static readonly Color Surface = Color.FromArgb(20, 29, 42);
        private static readonly Color Border = Color.FromArgb(43, 57, 74);
        private static readonly Color White = Color.FromArgb(235, 242, 251);
        private static readonly Color Muted = Color.FromArgb(143, 160, 181);
        private static readonly Color Mint = Color.FromArgb(97, 226, 180);
        private readonly ComboBox aircraft = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 43, 59), ForeColor = White,
            Font = new Font("Segoe UI", 13), IntegralHeight = false,
            AccessibleName = "Aircraft"
        };
        private readonly Button connection = new Button
        {
            Text = "Connect panels", FlatStyle = FlatStyle.Flat, BackColor = Mint,
            ForeColor = Background, Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Cursor = Cursors.Hand, AccessibleName = "Connect panels"
        };
        private readonly Button test = new Button
        {
            Text = "Open Test workspace  >", FlatStyle = FlatStyle.Flat,
            BackColor = Surface, ForeColor = Muted, Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand
        };
        private readonly Action<string> saveAircraft;
        private readonly Button quickTest = new Button
        {
            Text = "Quick test", FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Mint,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Enabled = false
        };
        private readonly Button simulator = new Button
        {
            Text = "Connect MSFS", FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Mint,
            Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
        };
        private string simulatorStatus = "MSFS disconnected / connect to detect your aircraft";
        private string detectedAircraft;
        private string detectedTitle;
        private bool connected;
        private bool fcu;
        private bool efis;
        private bool overhead;
        private string port;
        private string error;

        public StartupPanel(string selectedAircraft, Action<string> save, Action toggleConnection, Action openTest,
            Action runQuickTest, Action toggleSimulator)
        {
            Dock = DockStyle.Fill;
            BackColor = Background;
            DoubleBuffered = true;
            saveAircraft = save;
            aircraft.Items.AddRange(new object[] { AircraftProfiles.Automatic, "Airbus A320neo", "Airbus A321neo",
                "Airbus A330-900", AircraftProfiles.Caravan });
            aircraft.SelectedItem = selectedAircraft;
            if (aircraft.SelectedIndex < 0) aircraft.SelectedIndex = 0;
            aircraft.SelectedIndexChanged += delegate { saveAircraft(Convert.ToString(aircraft.SelectedItem)); Invalidate(); };
            connection.FlatAppearance.BorderSize = 0;
            connection.Click += delegate { toggleConnection(); };
            quickTest.FlatAppearance.BorderColor = Border;
            quickTest.Click += delegate { runQuickTest(); };
            simulator.FlatAppearance.BorderColor = Border;
            simulator.Click += delegate { toggleSimulator(); };
            test.FlatAppearance.BorderSize = 0;
            test.Click += delegate { openTest(); };
            Controls.Add(aircraft);
            Controls.Add(connection);
            Controls.Add(test);
            Controls.Add(quickTest);
            Controls.Add(simulator);
        }

        public bool AutomaticSelection { get { return Convert.ToString(aircraft.SelectedItem) == AircraftProfiles.Automatic; } }
        public string SelectedAircraft { get { return AutomaticSelection ? detectedAircraft : Convert.ToString(aircraft.SelectedItem); } }
        public bool CaravanSelected { get { return SelectedAircraft == AircraftProfiles.Caravan; } }

        public bool UpdateDetectedAircraft(string title)
        {
            if (detectedTitle == title) return false;
            string previous = SelectedAircraft;
            detectedTitle = title;
            detectedAircraft = AircraftProfiles.Detect(title);
            Invalidate();
            return previous != SelectedAircraft;
        }

        public void SelectAircraft(string name)
        {
            if (!aircraft.Items.Contains(name)) throw new ArgumentException("Unknown aircraft: " + name);
            aircraft.SelectedItem = name;
        }

        public void UpdateSimulator(bool isConnected, string message, bool testing, bool hardwareReady)
        {
            string text = isConnected ? "Disconnect MSFS" : "Connect MSFS";
            bool changed = simulatorStatus != message || simulator.Text != text;
            simulatorStatus = message;
            simulator.Text = text;
            quickTest.Enabled = hardwareReady || testing;
            quickTest.Text = testing ? "Stop quick test" : "Quick test";
            if (changed) Invalidate();
        }

        public void UpdateConnection(bool isConnected, string portName, PanelConnections panels, string lastError)
        {
            if (connected == isConnected && port == portName && fcu == panels.Fcu
                && efis == panels.Efis && overhead == panels.Overhead && error == lastError) return;
            connected = isConnected;
            port = portName;
            fcu = panels.Fcu;
            efis = panels.Efis;
            overhead = panels.Overhead;
            error = lastError;
            connection.Text = connected ? "Disconnect panels" : "Connect panels";
            connection.AccessibleName = connection.Text;
            AccessibleDescription = "miniFCU " + State(fcu) + ", miniEFIS " + State(efis)
                + ", miniOVERHEAD " + State(overhead) + (error == null ? "" : ". Error: " + error);
            Invalidate();
        }

        private string State(bool online)
        {
            return connected && online ? "Connected" : connected ? "Waiting for panel" : "Not connected";
        }

        private int ContentLeft { get { return Math.Max(30, (ClientSize.Width - 1120) / 2); } }
        private int ContentWidth { get { return ClientSize.Width - ContentLeft * 2; } }
        private int HeroTop { get { return 158; } }
        private int HeroHeight { get { return Math.Max(240, Math.Min(300, ClientSize.Height - 465)); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int left = ContentLeft;
            int width = ContentWidth;
            int selectionWidth = Math.Max(310, (int)(width * .39));
            aircraft.SetBounds(left + 26, HeroTop + 101, selectionWidth - 52, 36);
            simulator.SetBounds(left + 26, HeroTop + 144, selectionWidth - 52, 30);
            connection.SetBounds(left + 26, HeroTop + HeroHeight - 54, selectionWidth - 52, 32);
            quickTest.SetBounds(left + width - 318, HeroTop + HeroHeight + 18, 150, 32);
            test.SetBounds(left + width - 230, HeroTop + HeroHeight + 215, 230, 36);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int left = ContentLeft;
            int width = ContentWidth;
            int top = HeroTop;
            int heroHeight = HeroHeight;
            int selectionWidth = Math.Max(310, (int)(width * .39));

            using (var glow = new LinearGradientBrush(ClientRectangle, Background, Color.FromArgb(17, 29, 43), 25f))
                g.FillRectangle(glow, ClientRectangle);
            DrawBrand(g, left, 25);
            string link = connected ? port + " / SERIAL LINK OPEN" : "HARDWARE STANDBY";
            DrawBadge(g, new Rectangle(left + width - 236, 24, 236, 30), link, connected ? Mint : Muted);
            DrawText(g, "Your cockpit, ready at a glance.", 28, FontStyle.Bold, White, new Rectangle(left, 70, width, 48));
            DrawText(g, "Choose your aircraft. Check your panels. Make yourself at home.", 11, FontStyle.Regular, Muted,
                new Rectangle(left + 1, 123, width, 26));

            DrawCard(g, new Rectangle(left, top, selectionWidth, heroHeight), Surface);
            DrawText(g, "01  /  YOUR AIRCRAFT", 9, FontStyle.Bold, Mint, new Rectangle(left + 26, top + 24, selectionWidth - 52, 22));
            DrawText(g, "What are you flying?", 17, FontStyle.Bold, White, new Rectangle(left + 26, top + 53, selectionWidth - 52, 35));

            int artLeft = left + selectionWidth + 18;
            int artWidth = width - selectionWidth - 18;
            DrawAircraft(g, new Rectangle(artLeft, top, artWidth, heroHeight));
            int cardsTop = top + heroHeight + 64;
            DrawText(g, "02  /  YOUR PANELS", 9, FontStyle.Bold, Muted, new Rectangle(left, cardsTop - 40, 240, 24));
            int count = connected ? (fcu ? 1 : 0) + (efis ? 1 : 0) + (overhead ? 1 : 0) : 0;
            DrawText(g, count + " / 3 connected", 10, FontStyle.Regular, count == 3 ? Mint : Muted,
                new Rectangle(left + width - 160, cardsTop - 40, 160, 24), ContentAlignment.MiddleRight);
            int cardWidth = (width - 32) / 3;
            DrawPanelCard(g, new Rectangle(left, cardsTop, cardWidth, 130), "miniEFIS", "Electronic flight instruments", 1, efis);
            DrawPanelCard(g, new Rectangle(left + cardWidth + 16, cardsTop, cardWidth, 130), "miniFCU", "Flight control unit", 0, fcu);
            DrawPanelCard(g, new Rectangle(left + (cardWidth + 16) * 2, cardsTop, width - (cardWidth + 16) * 2, 130),
                "miniOVERHEAD", "Overhead panel", 2, overhead);

            int footer = cardsTop + 151;
            string message = error != null ? "Connection issue: " + error
                : !connected ? "Panels are offline. Connect to check your hardware."
                : count == 3 ? "All panels connected. Your hardware is ready."
                : "Listening for panel confirmations. Keep your panels powered on.";
            DrawText(g, message, 10, FontStyle.Regular, error != null ? Color.FromArgb(255, 174, 145) : count == 3 ? Mint : Muted,
                new Rectangle(left, footer, width - 250, 44));
            DrawText(g, simulatorStatus, 8, FontStyle.Regular, Muted,
                new Rectangle(left, footer + 49, width, 22));
        }

        private static void DrawBrand(Graphics g, int x, int y)
        {
            using (var pen = new Pen(Mint, 2))
            {
                g.DrawLine(pen, x, y + 15, x + 11, y + 4);
                g.DrawLine(pen, x + 11, y + 4, x + 22, y + 15);
                g.DrawLine(pen, x + 5, y + 16, x + 11, y + 10);
                g.DrawLine(pen, x + 11, y + 10, x + 17, y + 16);
            }
            DrawText(g, "miniCOCKPIT", 12, FontStyle.Bold, White, new Rectangle(x + 33, y - 1, 210, 27));
        }

        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawCard(Graphics g, Rectangle bounds, Color color)
        {
            using (var path = Rounded(bounds, 12))
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        private static void DrawText(Graphics g, string text, float size, FontStyle style, Color color, Rectangle bounds,
            ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            using (var font = new Font("Segoe UI", size, style))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
                if (alignment == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
                if (alignment == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter;
                TextRenderer.DrawText(g, text, font, bounds, color, flags);
            }
        }

        private static void DrawBadge(Graphics g, Rectangle bounds, string text, Color color)
        {
            using (var path = Rounded(bounds, 14))
            using (var brush = new SolidBrush(Color.FromArgb(25, 39, 51)))
                g.FillPath(brush, path);
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, bounds.X + 13, bounds.Y + 12, 6, 6);
            DrawText(g, text, 8, FontStyle.Bold, color, new Rectangle(bounds.X + 28, bounds.Y, bounds.Width - 36, bounds.Height));
        }

        private void DrawPanelCard(Graphics g, Rectangle bounds, string title, string subtitle, int kind, bool online)
        {
            bool ready = connected && online;
            Color indicator = ready ? Mint : connected ? Color.FromArgb(232, 183, 104) : Color.FromArgb(100, 116, 136);
            DrawCard(g, bounds, Surface);
            DrawPanelIcon(g, new Rectangle(bounds.X + 20, bounds.Y + 20, 48, 31), kind, ready ? Mint : Muted);
            DrawText(g, title, 14, FontStyle.Bold, White, new Rectangle(bounds.X + 82, bounds.Y + 16, bounds.Width - 98, 29));
            DrawText(g, subtitle, 9, FontStyle.Regular, Muted, new Rectangle(bounds.X + 82, bounds.Y + 45, bounds.Width - 98, 23));
            using (var pen = new Pen(Border))
                g.DrawLine(pen, bounds.X + 20, bounds.Y + 81, bounds.Right - 20, bounds.Y + 81);
            using (var halo = new SolidBrush(Color.FromArgb(30, indicator)))
            using (var dot = new SolidBrush(indicator))
            {
                g.FillEllipse(halo, bounds.X + 18, bounds.Y + 96, 17, 17);
                g.FillEllipse(dot, bounds.X + 23, bounds.Y + 101, 7, 7);
            }
            DrawText(g, State(online), 10, FontStyle.Bold, indicator,
                new Rectangle(bounds.X + 44, bounds.Y + 91, bounds.Width - 60, 28));
        }

        private static void DrawPanelIcon(Graphics g, Rectangle b, int kind, Color color)
        {
            using (var pen = new Pen(color, 1.4f))
            {
                g.DrawRectangle(pen, b);
                if (kind == 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        g.DrawRectangle(pen, b.X + 5 + i * 10, b.Y + 5, 7, 7);
                        g.DrawEllipse(pen, b.X + 6 + i * 10, b.Y + 19, 5, 5);
                    }
                }
                else if (kind == 1)
                {
                    g.DrawEllipse(pen, b.X + 6, b.Y + 8, 15, 15);
                    g.DrawEllipse(pen, b.X + 28, b.Y + 8, 12, 12);
                    g.DrawLine(pen, b.X + 13, b.Y + 8, b.X + 13, b.Y + 15);
                    g.DrawLine(pen, b.X + 34, b.Y + 8, b.X + 34, b.Y + 14);
                }
                else
                {
                    for (int row = 0; row < 2; row++)
                        for (int col = 0; col < 4; col++)
                            g.DrawRectangle(pen, b.X + 6 + col * 10, b.Y + 6 + row * 12, 6, 7);
                }
            }
        }

        private void DrawAircraft(Graphics g, Rectangle bounds)
        {
            DrawCard(g, bounds, Color.FromArgb(17, 29, 42));
            var state = g.Save();
            g.SetClip(bounds);
            int cx = bounds.X + bounds.Width / 2;
            int cy = bounds.Y + bounds.Height / 2;
            using (var gridPen = new Pen(Color.FromArgb(28, 47, 63)))
            {
                for (int x = bounds.X + 22; x < bounds.Right; x += 28)
                    g.DrawLine(gridPen, x, bounds.Y, x, bounds.Bottom);
                for (int y = bounds.Y + 20; y < bounds.Bottom; y += 28)
                    g.DrawLine(gridPen, bounds.X, y, bounds.Right, y);
            }
            using (var pen = new Pen(Color.FromArgb(48, 83, 96)))
            {
                g.DrawEllipse(pen, cx - 96, cy - 96, 192, 192);
                pen.DashStyle = DashStyle.Dash;
                g.DrawEllipse(pen, cx - 120, cy - 120, 240, 240);
                g.DrawLine(pen, cx - 145, cy, cx + 145, cy);
                g.DrawLine(pen, cx, cy - 130, cx, cy + 130);
            }
            bool caravan = CaravanSelected;
            PointF[] shape = caravan
                ? new[] { new PointF(0,-94), new PointF(7,-76), new PointF(8,-20), new PointF(108,-16), new PointF(108,-2),
                    new PointF(8,3), new PointF(6,65), new PointF(34,79), new PointF(34,88), new PointF(0,79),
                    new PointF(-34,88), new PointF(-34,79), new PointF(-6,65), new PointF(-8,3), new PointF(-108,-2),
                    new PointF(-108,-16), new PointF(-8,-20), new PointF(-7,-76) }
                : new[] { new PointF(0,-104), new PointF(7,-93), new PointF(10,-72), new PointF(10,-25),
                    new PointF(113,35), new PointF(115,48), new PointF(10,13), new PointF(7,72),
                    new PointF(39,92), new PointF(40,101), new PointF(0,89), new PointF(-40,101),
                    new PointF(-39,92), new PointF(-7,72), new PointF(-10,13), new PointF(-115,48),
                    new PointF(-113,35), new PointF(-10,-25), new PointF(-10,-72), new PointF(-7,-93) };
            float scale = Math.Min(bounds.Width / 390f, bounds.Height / 310f);
            g.TranslateTransform(cx, cy + 2);
            g.ScaleTransform(scale, scale);
            using (var path = new GraphicsPath())
            using (var fill = new SolidBrush(Color.FromArgb(34, 65, 76)))
            using (var outline = new Pen(Color.FromArgb(109, 191, 190), 1.5f))
            {
                path.AddPolygon(shape);
                g.FillPath(fill, path);
                g.DrawPath(outline, path);
                g.DrawLine(outline, 0, -78, 0, 78);
                if (!caravan)
                {
                    g.DrawRectangle(outline, -55, 6, 10, 25);
                    g.DrawRectangle(outline, 45, 6, 10, 25);
                }
                else g.DrawLine(outline, -21, -81, 21, -81);
            }
            g.Restore(state);
            DrawText(g, "AIRCRAFT PROFILE", 8, FontStyle.Bold, Muted, new Rectangle(bounds.X + 22, bounds.Y + 15, bounds.Width - 44, 22));
            string profile = SelectedAircraft ?? (String.IsNullOrWhiteSpace(detectedTitle) ? "Waiting for MSFS aircraft" : "Aircraft not recognized");
            DrawText(g, profile, 11, FontStyle.Bold, White,
                new Rectangle(bounds.X + 22, bounds.Bottom - 58, bounds.Width - 44, 28));
            string detail = AutomaticSelection
                ? (String.IsNullOrWhiteSpace(detectedTitle) ? "Automatic detection / manual choice available above" : "Detected: " + detectedTitle)
                : "Manual override / choose Auto-detect to follow MSFS";
            DrawText(g, detail, 8, FontStyle.Regular, Muted,
                new Rectangle(bounds.X + 22, bounds.Bottom - 29, bounds.Width - 44, 20));
            DrawBadge(g, new Rectangle(bounds.Right - 134, bounds.Y + 15, 112, 26),
                AutomaticSelection ? "AUTO" : "MANUAL", Mint);
        }
    }
}
