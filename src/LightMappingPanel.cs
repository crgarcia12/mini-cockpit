using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MiniCockpitLab
{
    internal sealed class LightMapping
    {
        public string Panel { get; set; }
        public string Description { get; set; }
        public string OnCommand { get; set; }
        public string OffCommand { get; set; }
    }

    internal sealed class LightChannel
    {
        public string Id;
        public string Panel;
        public string OnCommand;
        public string OffCommand;
        public string Hint;
        public bool IsScreen;
    }

    internal sealed class LightMappingPanel : UserControl
    {
        private readonly string mappingPath;
        private readonly string backupPath;
        private readonly Action<string> send;
        private readonly Action<Action> run;
        private readonly Action<string> log;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<LightChannel> channels = CreateChannels();
        private Dictionary<string, LightMapping> mappings = new Dictionary<string, LightMapping>();
        private readonly TextBox description = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Segoe UI", 12) };
        private readonly Label activeLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Click START to test the first unmapped output." };
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
        };
        private int active = -1;
        private readonly System.Windows.Forms.Timer sweepTimer = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly Stopwatch sweepClock = new Stopwatch();
        private double nextSweepAt;
        private int sweepStep;
        private int sweepTouched;
        private bool sweepRunning;
        private readonly Button playAll;
        private readonly System.Windows.Forms.Timer quickTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        public bool QuickTestRunning { get; private set; }
        public bool IsTesting { get { return active >= 0 || sweepRunning || QuickTestRunning; } }

        public LightMappingPanel(string path, Action<string> transmit, Action<Action> execute, Action<string> writeLog)
        {
            Dock = DockStyle.Fill;
            mappingPath = path;
            backupPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "minicockpit-light-mappings.json"));
            send = transmit;
            run = execute;
            log = writeLog;
            quickTimer.Tick += delegate { run(StopQuickTest); };
            if (File.Exists(path))
            {
                mappings = json.Deserialize<Dictionary<string, LightMapping>>(File.ReadAllText(path));
                if (mappings == null) throw new FormatException("Invalid light mappings file: " + path);
                foreach (var saved in mappings)
                {
                    LightChannel channel = channels.Find(c => c.Id == saved.Key);
                    if (channel == null || saved.Value == null || String.IsNullOrWhiteSpace(saved.Value.Description)
                        || saved.Value.OnCommand != channel.OnCommand || saved.Value.OffCommand != channel.OffCommand)
                        throw new FormatException("Invalid light mapping: " + saved.Key);
                }
            }

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            AddButton(toolbar, "START / resume", Start);
            AddButton(toolbar, "SAVE + NEXT (Tab)", Next);
            AddButton(toolbar, "STOP / reset test", Stop);
            AddButton(toolbar, "ALL LIGHTS OFF", AllOff);
            playAll = AddButton(toolbar, "PLAY ALL (100 ms)", delegate
            {
                if (sweepRunning) Stop();
                else StartSweep();
            });
            sweepTimer.Tick += delegate
            {
                sweepTimer.Stop();
                bool completed = false;
                try { run(delegate { AdvanceSweep(); completed = true; }); }
                finally
                {
                    if (completed && sweepRunning) sweepTimer.Start();
                    else PauseSweep();
                }
            };
            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Name the changed indicator or display, then TAB. Blank = skip. LCD tests use sample values; STOP restores their baseline."
            }, 0, 1);
            layout.Controls.Add(description, 0, 2);
            layout.Controls.Add(activeLabel, 0, 3);
            grid.Columns.Add("panel", "Panel");
            grid.Columns.Add("channel", "Output channel");
            grid.Columns.Add("description", "Saved description");
            grid.Columns.Add("hint", "DLS function / sample");
            grid.Columns.Add("on", "Test commands");
            grid.Columns.Add("off", "Off / restore commands");
            grid.Columns["description"].FillWeight = 200;
            foreach (LightChannel channel in channels)
            {
                LightMapping mapping;
                grid.Rows.Add(channel.Panel, channel.Id,
                    mappings.TryGetValue(channel.Id, out mapping) ? mapping.Description : "", channel.Hint ?? "",
                    channel.OnCommand.Replace("\n", " "), channel.OffCommand.Replace("\n", " "));
            }
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0) run(delegate { Activate(args.RowIndex); });
            };
            layout.Controls.Add(grid, 0, 4);
            Controls.Add(layout);
        }

        private static List<LightChannel> CreateChannels()
        {
            var result = new List<LightChannel>();
            foreach (int address in new[] { 16, 24, 17, 25, 12, 20, 11, 19, 10, 18, 13, 21, 15, 23, 14, 22 })
            {
                string prefix = "\\" + address.ToString(CultureInfo.InvariantCulture);
                result.Add(new LightChannel { Id = "overhead." + address, Panel = "Overhead", OnCommand = prefix + "1,", OffCommand = prefix + "0," });
            }
            string[] on = { "P,", "U,", "T,", "L,", "E,", "R," };
            string[] off = { "p,", "u,", "t,", "l,", "e,", "r," };
            for (int i = 0; i < on.Length; i++)
                result.Add(new LightChannel { Id = "fcu." + (i + 1), Panel = "FCU", OnCommand = on[i], OffCommand = off[i] });
            foreach (string prefix in new[] { "5", "4", "3", "2", "1", "0", "!" })
                result.Add(new LightChannel { Id = "efis." + prefix, Panel = "EFIS", OnCommand = prefix + "1,", OffCommand = prefix + "0," });
            const string pressureBaseline = "{1,\n@1,\n#1013,";
            Action<string, string, string, string> efisScreen = (id, hint, test, restore) =>
                result.Add(new LightChannel { Id = "screen.efis." + id, Panel = "EFIS LCD", Hint = hint,
                    OnCommand = test, OffCommand = restore, IsScreen = true });
            efisScreen("qnh", "QNH / 1013 hPa", pressureBaseline, "{1,\n@0,\n#1013,");
            efisScreen("qfe", "QFE / 1013 hPa", "{1,\n@0,\n#1013,", pressureBaseline);
            efisScreen("std", "STD", "{0,\n@2,", pressureBaseline);
            efisScreen("hpa", "Pressure digits: 1025 hPa", "{1,\n@1,\n#1025,", pressureBaseline);
            efisScreen("inhg", "Pressure digits / decimal point: 29.92 inHg", "{1,\n@1,\n_2992,", pressureBaseline);
            Action<string, string, string, string> fcuScreen = (id, hint, test, restore) =>
                result.Add(new LightChannel { Id = "screen.fcu." + id, Panel = "FCU LCD", Hint = hint,
                    OnCommand = test, OffCommand = restore, IsScreen = true });
            fcuScreen("speed", "Speed digits / SPD: 123", "I,\nS123,", "I,\nS0,");
            fcuScreen("mach", "Mach digits / decimal point: 0.78", "I,\nM78,", "I,\nS123,");
            fcuScreen("heading", "Heading digits: 234", "O,\nH234,", "O,\nH0,");
            fcuScreen("altitude", "Altitude digits: 12300", "A12300,", "A0,");
            fcuScreen("vs", "Vertical speed / V/S: +900", "W,\nV900,", "W,\nV0,");
            fcuScreen("vs-negative", "Negative vertical speed / minus sign: -700", "W,\nV-700,", "W,\nV0,");
            fcuScreen("fpa", "Flight path angle / FPA / decimal point", "W,\nF250,", "W,\nV0,");
            fcuScreen("fpa-negative", "Negative flight path angle / minus sign", "W,\nF-250,", "W,\nV0,");
            fcuScreen("speed-dashes", "Speed dashes", "i,\nd,", "I,\nS123,");
            fcuScreen("mach-dashes", "Mach dashes", "i,\nD,", "I,\nS123,");
            fcuScreen("heading-dashes", "Heading dashes", "o,\nh,", "O,\nH234,");
            fcuScreen("vs-dashes", "V/S dashes", "w,\nv,", "W,\nV900,");
            fcuScreen("fpa-dashes", "FPA dashes", "w,\nf,", "W,\nV900,");
            fcuScreen("speed-dot", "Managed speed dot", "z,", "x,");
            fcuScreen("heading-dot", "Managed heading dot", "m,", "s,");
            fcuScreen("altitude-dot", "Managed altitude dot", "a,", "q,");
            foreach (var pair in new[] { new[] { "I,", "i," }, new[] { "Y,", "y," }, new[] { "W,", "w," },
                new[] { "O,", "o," } })
                fcuScreen("mode-" + pair[0][0], "LCD mode pair " + pair[0] + " / " + pair[1] + " - identify on hardware",
                    pair[0], pair[1]);
            return result;
        }

        private Button AddButton(FlowLayoutPanel toolbar, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += delegate { run(action); };
            toolbar.Controls.Add(button);
            return button;
        }

        public void Start()
        {
            AllOff();
            int index = channels.FindIndex(c => !mappings.ContainsKey(c.Id));
            Activate(index < 0 ? 0 : index);
        }

        public void Activate(int index)
        {
            if (index < 0 || index >= channels.Count) throw new ArgumentOutOfRangeException("index");
            SaveDescription();
            StopQuickTest();
            StopSweep();
            if (active >= 0) TransmitCommands(channels[active].OffCommand);
            active = -1;
            description.ReadOnly = true;
            LightChannel channel = channels[index];
            TransmitCommands(channel.OnCommand);
            active = index;
            LightMapping mapping;
            description.Text = mappings.TryGetValue(channel.Id, out mapping) ? mapping.Description : "";
            description.ReadOnly = false;
            activeLabel.Text = (index + 1) + " / " + channels.Count + " - " + channel.Id
                + " TEST ACTIVE. " + (channel.Hint ?? "Name the illuminated indicator, not the button press.");
            grid.CurrentCell = grid.Rows[index].Cells["description"];
            grid.FirstDisplayedScrollingRowIndex = index;
            description.Focus();
            description.SelectAll();
        }

        public void SaveDescription()
        {
            if (active < 0) return;
            string label = description.Text.Trim();
            if (label.Length == 0) return;
            LightChannel channel = channels[active];
            LightMapping existing;
            if (mappings.TryGetValue(channel.Id, out existing) && existing.Description == label) return;
            var updated = new Dictionary<string, LightMapping>(mappings);
            updated[channel.Id] = new LightMapping
            {
                Panel = channel.Panel, Description = label, OnCommand = channel.OnCommand, OffCommand = channel.OffCommand
            };
            string text = json.Serialize(updated);
            AtomicWrite(mappingPath, text);
            AtomicWrite(backupPath, text);
            mappings = updated;
            grid.Rows[active].Cells["description"].Value = label;
            log("SAVED LIGHT " + channel.Id + " = " + label);
        }

        private static void AtomicWrite(string path, string text)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public void Next()
        {
            if (active < 0) { Start(); return; }
            if (active + 1 < channels.Count) Activate(active + 1);
            else
            {
                Stop();
                activeLabel.Text = "Finished all " + channels.Count + " channels. Click a row to retest or START to revisit skipped lights.";
            }
        }

        public void Stop()
        {
            SaveDescription();
            StopQuickTest();
            StopSweep();
            if (active >= 0) TransmitCommands(channels[active].OffCommand);
            active = -1;
            description.ReadOnly = true;
            activeLabel.Text = "Stopped. Description saved; current test switched off / restored.";
        }

        public void AllOff()
        {
            SaveDescription();
            StopQuickTest();
            StopSweep();
            if (active >= 0 && channels[active].IsScreen) TransmitCommands(channels[active].OffCommand);
            foreach (LightChannel channel in channels)
                if (!channel.IsScreen) TransmitCommands(channel.OffCommand);
            active = -1;
            description.ReadOnly = true;
            activeLabel.Text = "All button annunciators off; active LCD test restored.";
        }

        public void StartQuickTest()
        {
            AllOff();
            QuickTestRunning = true;
            foreach (LightChannel channel in channels)
                if (!channel.IsScreen) TransmitCommands(channel.OnCommand);
            quickTimer.Start();
            log("QUICK TEST: all 29 button annunciators ON for three seconds.");
        }

        public void StopQuickTest()
        {
            if (!QuickTestRunning) return;
            PauseQuickTest();
            foreach (LightChannel channel in channels)
                if (!channel.IsScreen) TransmitCommands(channel.OffCommand);
            log("QUICK TEST COMPLETE: all button annunciators OFF; simulator indicators can resume.");
        }

        public void PauseQuickTest()
        {
            quickTimer.Stop();
            QuickTestRunning = false;
        }

        public void StartSweep()
        {
            AllOff();
            sweepStep = 0;
            sweepClock.Restart();
            nextSweepAt = 0;
            sweepRunning = true;
            playAll.Text = "STOP";
            log("OUTPUT SWEEP START: all ON, then all OFF / restored, 100 ms per output.");
            AdvanceSweep();
            if (sweepRunning) sweepTimer.Start();
        }

        private void AdvanceSweep()
        {
            if (sweepClock.Elapsed.TotalMilliseconds < nextSweepAt) return;
            bool turnOn = sweepStep < channels.Count;
            int index = sweepStep % channels.Count;
            LightChannel channel = channels[index];
            if (turnOn) sweepTouched = index + 1;
            TransmitCommands(turnOn ? channel.OnCommand : channel.OffCommand);
            activeLabel.Text = "SWEEP " + (turnOn ? "ON " : "OFF / RESTORE ")
                + (index + 1) + " / " + channels.Count + " - " + channel.Id;
            grid.CurrentCell = grid.Rows[index].Cells["description"];
            grid.FirstDisplayedScrollingRowIndex = index;
            sweepStep++;
            nextSweepAt = sweepClock.Elapsed.TotalMilliseconds + 100;
            if (sweepStep == channels.Count * 2)
            {
                PauseSweep();
                sweepTouched = 0;
                activeLabel.Text = "Sweep complete: all outputs tested ON, then OFF / restored.";
                log("OUTPUT SWEEP COMPLETE");
            }
        }

        public void PauseSweep()
        {
            sweepTimer.Stop();
            sweepRunning = false;
            playAll.Text = "PLAY ALL (100 ms)";
        }

        private void StopSweep()
        {
            PauseSweep();
            if (sweepTouched == 0) return;
            for (int i = 0; i < sweepTouched; i++) TransmitCommands(channels[i].OffCommand);
            sweepTouched = 0;
            log("OUTPUT SWEEP CANCELLED: reached outputs switched OFF / restored.");
        }

        private void TransmitCommands(string commands)
        {
            foreach (string command in commands.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                send(command);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { sweepTimer.Dispose(); quickTimer.Dispose(); }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Tab && description.Focused)
            {
                run(Next);
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }
    }
}
