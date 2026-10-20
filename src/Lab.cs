using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MiniCockpitLab
{
    internal sealed class Counter
    {
        public string Code;
        public long Count;
        public long TrialStart;
        public DateTime LastSeen;
        public DataGridViewRow Row;
        public InputFrame LastFrame;
    }

    internal sealed class LabWindow : Form
    {
        private readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly TokenParser parser = new TokenParser();
        private readonly Dictionary<string, Counter> counters = new Dictionary<string, Counter>();
        private Dictionary<string, string> mappings = new Dictionary<string, string>();
        private readonly ComboBox ports = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox baud = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox suffix = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button connect = new Button { Text = "Connect", AutoSize = true };
        private readonly Button initialize = new Button { Text = "Initialize (C)", AutoSize = true };
        private readonly CheckBox autoInitialize = new CheckBox { Text = "Initialize after connecting", Checked = true, AutoSize = true };
        private readonly CheckBox showRaw = new CheckBox { Text = "Show raw RX", Checked = false, AutoSize = true };
        private readonly TextBox command = new TextBox { Width = 260 };
        private readonly TextBox trialInput = new TextBox { Width = 410, Text = "Touch a control, type its description in the list, TAB to save" };
        private readonly TextBox logView = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Label lastInput = new Label { Dock = DockStyle.Top, Height = 37, Text = "LAST INPUT: waiting", Font = new Font("Segoe UI", 13, FontStyle.Bold), BackColor = Color.FromArgb(225, 240, 255), Padding = new Padding(5) };
        private readonly TextBox learnInput = new TextBox { Width = 350, Text = "Name the next control you will move" };
        private readonly Button learnButton = new Button { Text = "LEARN NEXT INPUT", AutoSize = true };
        private readonly Button nameLastButton = new Button { Text = "NAME LAST INPUT...", AutoSize = true, Enabled = false, BackColor = Color.LightGoldenrodYellow };
        private readonly CheckBox automaticMapping = new CheckBox { Text = "Focus input description (F8)", Checked = true, AutoSize = true };
        private readonly TextBox batchText = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 11), Text = "S123\r\nH234\r\nA12300\r\nV-700" };
        private readonly NumericUpDown batchGap = new NumericUpDown { Minimum = 100, Maximum = 10000, Increment = 100, Value = 500, Width = 80 };
        private readonly Queue<string> batchCommands = new Queue<string>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
        private SerialPort serial;
        private StreamWriter capture;
        private string sessionPath;
        private string currentTrial = "Touch a control, type its description in the list, TAB to save";
        private string lastError;
        private long bytesReceived;
        private DateTime? lastReceive;
        private DateTime? initializeAt;
        private DateTime nextSnapshot;
        private bool dirty;
        private bool closing;
        private string learnLabel;
        private DateTime nextBatchAt;
        private InputFrame lastFrame;
        private string previousPanel = "FCU";
        private DateTime automaticMappingReadyAt;
        private bool descriptionSaveFailed;
        private readonly LightMappingPanel lightMapping;
        private readonly StartupPanel startup;
        private readonly PanelConnections panelConnections = new PanelConnections();
        private readonly TabControl navigation;
        private readonly TabControl testWorkspace;
        private readonly HardwarePanel hardwareView;
        private readonly SimulatorBridge simulator;
        private readonly CaravanProfile caravan = new CaravanProfile();
        private readonly Dictionary<string, string> simulatorOutputs = new Dictionary<string, string>();

        public LabWindow(bool startConnected, bool autoConnectSimulator = true)
        {
            Text = "miniCOCKPIT";
            Width = 1280;
            Height = 850;
            MinimumSize = new Size(1050, 800);
            Font = new Font("Segoe UI", 9);
            StartPosition = FormStartPosition.CenterScreen;
            Directory.CreateDirectory(Path.Combine(folder, "captures"));
            sessionPath = Path.Combine(folder, "captures", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
            capture = new StreamWriter(sessionPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            string mappingPath = Path.Combine(folder, "mappings.json");
            if (File.Exists(mappingPath))
                mappings = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
            simulator = new SimulatorBridge(delegate(string message) { WriteLog("simulator", new { message = message }); Append(message); });
            hardwareView = new HardwarePanel(Path.Combine(folder, "assets"));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            navigation = new TabControl
            {
                Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed, ItemSize = new Size(112, 42),
                Padding = new Point(20, 10)
            };
            navigation.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                bool selected = args.Index == navigation.SelectedIndex;
                using (var background = new SolidBrush(Color.FromArgb(12, 18, 28)))
                    args.Graphics.FillRectangle(background, args.Bounds);
                using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
                    TextRenderer.DrawText(args.Graphics, navigation.TabPages[args.Index].Text,
                        font, args.Bounds,
                        selected ? Color.FromArgb(97, 226, 180) : Color.FromArgb(143, 160, 181),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (selected)
                    using (var pen = new Pen(Color.FromArgb(97, 226, 180), 3))
                        args.Graphics.DrawLine(pen, args.Bounds.Left + 20, args.Bounds.Bottom - 3, args.Bounds.Right - 20, args.Bounds.Bottom - 3);
                if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
            };
            var startPage = new TabPage("Start") { BackColor = Color.FromArgb(12, 18, 28) };
            var testPage = new TabPage("Test");
            string aircraftPath = Path.Combine(folder, "aircraft.txt");
            startup = new StartupPanel(File.Exists(aircraftPath) ? File.ReadAllText(aircraftPath).Trim() : AircraftProfiles.Automatic,
                delegate(string selected)
                {
                    Run(delegate
                    {
                        AtomicWrite(aircraftPath, selected);
                        caravan.Reset();
                        simulatorOutputs.Clear();
                        hardwareView.SetSimulatorState(simulator.Fresh && startup.CaravanSelected && simulator.State.IsCaravan ? simulator.State : null);
                        UpdateStartup();
                    });
                },
                delegate { Run(delegate { if (IsConnected) Disconnect(); else Connect(); UpdateStartup(); }); },
                delegate { navigation.SelectedTab = testPage; },
                delegate { Run(ToggleQuickTest); },
                delegate { Run(ToggleSimulator); });
            startPage.Controls.Add(startup);
            testWorkspace = new TabControl { Dock = DockStyle.Fill };
            var hardwarePage = new TabPage("HARDWARE");
            hardwarePage.Controls.Add(hardwareView);
            var toolsPage = new TabPage("MAPPINGS / TOOLS");
            toolsPage.Controls.Add(root);
            testWorkspace.TabPages.Add(hardwarePage);
            testWorkspace.TabPages.Add(toolsPage);
            testPage.Controls.Add(testWorkspace);
            navigation.TabPages.Add(startPage);
            navigation.TabPages.Add(testPage);
            Controls.Add(navigation);

            FlowLayoutPanel connectionRow = Row();
            connectionRow.Controls.Add(Caption("Port"));
            connectionRow.Controls.Add(ports);
            connectionRow.Controls.Add(Caption("Baud (8N1)"));
            baud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            baud.SelectedIndex = 0;
            connectionRow.Controls.Add(baud);
            connectionRow.Controls.Add(ActionButton("Refresh ports", RefreshPorts));
            connect.Click += delegate { Run(delegate { if (IsConnected) Disconnect(); else Connect(); }); };
            connectionRow.Controls.Add(connect);
            initialize.Click += delegate { Run(InitializePanel); };
            connectionRow.Controls.Add(initialize);
            connectionRow.Controls.Add(autoInitialize);
            root.Controls.Add(connectionRow, 0, 0);

            FlowLayoutPanel displayRow = Row();
            displayRow.Controls.Add(Caption("FCU displays:"));
            AddDisplay(displayRow, "S", "Speed", 123, 0, 999);
            AddDisplay(displayRow, "H", "Heading", 234, 0, 359);
            AddDisplay(displayRow, "A", "Altitude", 12300, 0, 99999);
            AddDisplay(displayRow, "V", "V/S", 900, -9999, 9999);
            root.Controls.Add(displayRow, 0, 1);

            FlowLayoutPanel commandRow = Row();
            commandRow.Controls.Add(Caption("Exact ASCII command"));
            commandRow.Controls.Add(command);
            suffix.Items.AddRange(new object[] { "No suffix", "Semicolon", "CRLF" });
            suffix.SelectedIndex = 0;
            commandRow.Controls.Add(suffix);
            commandRow.Controls.Add(ActionButton("Send", delegate
            {
                string ending = suffix.SelectedIndex == 1 ? ";" : suffix.SelectedIndex == 2 ? "\r\n" : "";
                Send(command.Text + ending);
            }));
            commandRow.Controls.Add(showRaw);
            commandRow.Controls.Add(ActionButton("Open captures", delegate { Process.Start("explorer.exe", Path.Combine(folder, "captures")); }));
            root.Controls.Add(commandRow, 0, 2);

            FlowLayoutPanel trialRow = Row();
            trialRow.Controls.Add(Caption("Capture step"));
            trialRow.Controls.Add(trialInput);
            trialRow.Controls.Add(ActionButton("MARK / reset trial counts", delegate { Mark(trialInput.Text); }));
            trialRow.Controls.Add(Caption("Edit 'Observed mapping' only after testing. All bytes are saved."));
            root.Controls.Add(trialRow, 0, 3);

            grid.Columns.Add("code", "Event code");
            grid.Columns.Add("value", "Last value");
            grid.Columns.Add("total", "Total");
            grid.Columns.Add("trial", "Since MARK");
            grid.Columns.Add("last", "Last seen (local)");
            grid.Columns.Add("reference", "Old protocol reference (NOT confirmed)");
            grid.Columns.Add("mapping", "Description - type here");
            foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = column.Name != "mapping";
            grid.Columns["code"].FillWeight = 40;
            grid.Columns["value"].FillWeight = 55;
            grid.Columns["total"].FillWeight = 40;
            grid.Columns["trial"].FillWeight = 45;
            grid.Columns["last"].FillWeight = 70;
            grid.Columns["reference"].FillWeight = 230;
            grid.Columns["mapping"].FillWeight = 180;
            grid.CellEndEdit += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex < 0 || args.ColumnIndex != grid.Columns["mapping"].Index) return;
                descriptionSaveFailed = true;
                Run(delegate
                {
                    string code = Convert.ToString(grid.Rows[args.RowIndex].Cells["code"].Value);
                    string label = Convert.ToString(grid.Rows[args.RowIndex].Cells["mapping"].Value);
                    string previous = (counters[code].LastFrame ?? InputFrame.Parse(code)).FindMapping(mappings) ?? "";
                    if (String.IsNullOrWhiteSpace(label))
                    {
                        grid.Rows[args.RowIndex].Cells["mapping"].Value = previous;
                        Append("Blank description not saved for event " + code + "; existing label retained if present.");
                    }
                    else if (label.Trim() != previous)
                        SaveMapping(code, label, "inline description edit");
                    grid.Rows[args.RowIndex].ErrorText = "";
                    lastError = null;
                    descriptionSaveFailed = false;
                });
                if (descriptionSaveFailed) grid.Rows[args.RowIndex].ErrorText = lastError;
            };
            foreach (string key in mappings.Keys.Where(x => !String.IsNullOrWhiteSpace(mappings[x])).OrderBy(x => x))
            {
                InputFrame frame = InputFrame.Parse(key);
                if (counters.ContainsKey(frame.Code)) continue;
                int row = grid.Rows.Add(frame.Code, frame.Value ?? "", 0, 0, "", Baseline.Describe(frame.Code), frame.FindMapping(mappings));
                counters.Add(frame.Code, new Counter { Code = frame.Code, Row = grid.Rows[row], LastFrame = frame });
            }
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex < 0 || args.ColumnIndex == grid.Columns["mapping"].Index) return;
                string code = Convert.ToString(grid.Rows[args.RowIndex].Cells["code"].Value);
                Run(delegate { NameInput(counters[code].LastFrame ?? InputFrame.Parse(code)); });
            };
            FlowLayoutPanel quickRow = Row();
            quickRow.Controls.Add(automaticMapping);
            quickRow.Controls.Add(Caption("Touch any control -> its description cell focuses -> type a name -> TAB saves."));
            nameLastButton.Click += delegate { Run(delegate { NameInput(lastFrame); }); };
            quickRow.Controls.Add(nameLastButton);
            quickRow.Controls.Add(ActionButton("Name selected row...", delegate
            {
                if (grid.CurrentRow == null) throw new InvalidOperationException("Select an input row first.");
                string code = Convert.ToString(grid.CurrentRow.Cells["code"].Value);
                NameInput(counters[code].LastFrame ?? InputFrame.Parse(code));
            }));
            root.Controls.Add(quickRow, 0, 4);
            FlowLayoutPanel learningRow = Row();
            learningRow.Controls.Add(Caption("Learn a control:"));
            learningRow.Controls.Add(learnInput);
            learnButton.Click += delegate { Run(delegate { ArmLearning(learnInput.Text); }); };
            learningRow.Controls.Add(learnButton);
            learningRow.Controls.Add(ActionButton("Cancel learning", delegate { learnLabel = null; learnButton.Text = "LEARN NEXT INPUT"; Append("Learning cancelled."); }));
            learningRow.Controls.Add(Caption("Enter a name, click LEARN, then move ONE control. Saved automatically."));
            root.Controls.Add(learningRow, 0, 5);
            logView.Font = new Font("Consolas", 9);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1200, 500), Orientation = Orientation.Horizontal, SplitterDistance = 260 };
            split.Panel1.Controls.Add(grid);
            split.Panel1.Controls.Add(lastInput);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var liveTab = new TabPage("LIVE INPUT / TX LOG");
            liveTab.Controls.Add(logView);
            var consoleTab = new TabPage("COMMAND CONSOLE / BATCH");
            consoleTab.Controls.Add(BuildConsole());
            tabs.TabPages.Add(liveTab);
            tabs.TabPages.Add(consoleTab);
            split.Panel2.Controls.Add(tabs);
            lightMapping = new LightMappingPanel(Path.Combine(folder, "light-mappings.json"), Send, Run,
                delegate(string message) { WriteLog("light-mapping", new { message = message }); Append(message); });
            var workspace = new TabControl { Dock = DockStyle.Fill };
            var inputsPage = new TabPage("INPUT MAPPING");
            inputsPage.Controls.Add(split);
            var lightsPage = new TabPage("LIGHTS / SCREENS");
            lightsPage.Controls.Add(lightMapping);
            workspace.TabPages.Add(inputsPage);
            workspace.TabPages.Add(lightsPage);
            workspace.Selecting += delegate(object sender, TabControlCancelEventArgs args)
            {
                if (!FinishDescriptionEdit()) { args.Cancel = true; return; }
                if (workspace.SelectedTab == lightsPage)
                {
                    bool stopped = false;
                    Run(delegate { if (IsConnected) lightMapping.Stop(); else lightMapping.SaveDescription(); stopped = true; });
                    if (!stopped) args.Cancel = true;
                }
            };
            navigation.Selecting += delegate(object sender, TabControlCancelEventArgs args)
            {
                if (navigation.SelectedTab != testPage) return;
                if (!FinishDescriptionEdit()) { args.Cancel = true; return; }
                bool stopped = false;
                Run(delegate
                {
                    if (IsConnected) lightMapping.Stop(); else lightMapping.SaveDescription();
                    batchCommands.Clear();
                    learnLabel = null;
                    learnButton.Text = "LEARN NEXT INPUT";
                    stopped = true;
                });
                if (!stopped) args.Cancel = true;
            };
            testWorkspace.Selecting += delegate(object sender, TabControlCancelEventArgs args)
            {
                if (!FinishDescriptionEdit()) { args.Cancel = true; return; }
                bool stopped = false;
                Run(delegate
                {
                    if (IsConnected) lightMapping.Stop(); else lightMapping.SaveDescription();
                    batchCommands.Clear();
                    learnLabel = null;
                    learnButton.Text = "LEARN NEXT INPUT";
                    caravan.Reset();
                    simulatorOutputs.Clear();
                    stopped = true;
                });
                if (!stopped) args.Cancel = true;
            };
            simulator.StateChanged += delegate
            {
                bool profileChanged = startup.UpdateDetectedAircraft(simulator.Fresh ? simulator.State.Aircraft : null);
                hardwareView.SetSimulatorState(simulator.Fresh && startup.CaravanSelected && simulator.State.IsCaravan ? simulator.State : null);
                if (!simulator.Connected || profileChanged) { caravan.Reset(); simulatorOutputs.Clear(); }
                UpdateStartup();
            };
            root.Controls.Add(workspace, 0, 6);
            root.Controls.Add(status, 0, 7);

            RefreshPorts();
            WriteLog("session", new { pid = Process.GetCurrentProcess().Id, baseline = "janmalec/miniCockpit-serial@920b62cec507253682cf0990832b6dec814cf13d", simulatorIntegration = "SimConnect aircraft detection / Caravan controls" });
            Append("Capture: " + sessionPath);
            Append("Move controls freely: the LAST INPUT banner and log show each incoming token.");
            Append("INLINE MAPPING ON: touch a control; its description cell in the big list focuses automatically.");
            Append("Type a description and TAB to save. The next different input focuses its own row. F8 pauses focus-following.");
            Append("You can also double-click an input row. Values are shown separately from event codes.");
            Append("Send arbitrary ASCII above, or use COMMAND CONSOLE / BATCH for multiple commands.");
            Append("Unknown tokens are retained. All bytes are captured even when raw RX is hidden.");
            timer.Tick += delegate { Tick(); };
            timer.Start();
            Shown += delegate
            {
                if (startConnected) Run(Connect);
                if (autoConnectSimulator && startup.AutomaticSelection) Run(ToggleSimulator);
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (!FinishDescriptionEdit()) { args.Cancel = true; return; }
                bool stopped = false;
                Run(delegate { if (IsConnected) lightMapping.Stop(); else lightMapping.SaveDescription(); stopped = true; });
                if (!stopped) { args.Cancel = true; return; }
                closing = true;
                timer.Stop();
                Run(Disconnect);
                simulator.Dispose();
                capture.Dispose();
            };
        }

        private bool IsConnected { get { return serial != null && serial.IsOpen; } }
        private bool ToolsVisible { get { return navigation.SelectedIndex == 1 && testWorkspace.SelectedIndex == 1; } }
        private bool CanControlSimulator
        {
            get
            {
                return startup.CaravanSelected && simulator.Fresh && simulator.State.IsCaravan
                    && !ToolsVisible && !lightMapping.IsTesting && IsConnected && !initializeAt.HasValue
                    && DateTime.UtcNow >= automaticMappingReadyAt;
            }
        }

        private void ToggleQuickTest()
        {
            if (lightMapping.QuickTestRunning) { lightMapping.StopQuickTest(); return; }
            if (!IsConnected || initializeAt.HasValue)
                throw new InvalidOperationException("Connect panels and wait for initialization before Quick test.");
            batchCommands.Clear();
            lightMapping.StartQuickTest();
            simulatorOutputs.Clear();
            UpdateStartup();
        }

        private void ToggleSimulator()
        {
            if (simulator.Connected || simulator.Connecting) { simulator.Disconnect("MSFS disconnected"); return; }
            simulator.Connect(Handle);
            UpdateStartup();
        }

        private void HandleSimulatorInput(InputFrame frame)
        {
            if (!startup.CaravanSelected || !simulator.Connected || ToolsVisible || lightMapping.IsTesting
                || initializeAt.HasValue || DateTime.UtcNow < automaticMappingReadyAt) return;
            if (!simulator.Fresh) throw new InvalidOperationException("MSFS telemetry is stale; simulator control is paused.");
            foreach (SimulatorCommand command in caravan.Handle(frame, simulator.State)) simulator.Send(command);
        }

        private void SyncSimulator()
        {
            simulator.Tick();
            if (!CanControlSimulator)
            {
                simulatorOutputs.Clear();
                if (!simulator.Fresh) hardwareView.SetSimulatorState(null);
                return;
            }
            foreach (SimulatorCommand command in caravan.UpdateAutomaticLights(simulator.State)) simulator.Send(command);
            foreach (var output in CaravanProfile.HardwareOutputs(simulator.State))
            {
                string previous;
                if (simulatorOutputs.TryGetValue(output.Key, out previous) && previous == output.Value) continue;
                Send(output.Value);
                simulatorOutputs[output.Key] = output.Value;
            }
        }

        private static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 5, 0, 0) };
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(5, 7, 5, 0) };
        }

        private Button ActionButton(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += delegate { Run(action); };
            return button;
        }

        private void AddDisplay(FlowLayoutPanel row, string prefix, string label, int initial, int minimum, int maximum)
        {
            row.Controls.Add(Caption(label));
            var value = new NumericUpDown { Width = 75, Minimum = minimum, Maximum = maximum, Value = initial };
            row.Controls.Add(value);
            row.Controls.Add(ActionButton("Set", delegate { Send(prefix + value.Value.ToString(CultureInfo.InvariantCulture)); }));
        }

        private Control BuildConsole()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(5) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel toolbar = Row();
            toolbar.Controls.Add(ActionButton("SEND ALL LINES", delegate { StartBatch(batchText.Text); }));
            toolbar.Controls.Add(ActionButton("STOP BATCH", delegate { batchCommands.Clear(); Append("Batch cancelled."); }));
            toolbar.Controls.Add(Caption("Delay between commands (ms)"));
            toolbar.Controls.Add(batchGap);
            toolbar.Controls.Add(Caption("Suffix is selected in the Exact ASCII command row above."));
            layout.Controls.Add(toolbar, 0, 0);
            layout.SetColumnSpan(toolbar, 2);
            layout.Controls.Add(batchText, 0, 1);
            var help = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = "One exact ASCII command per line. Edit freely, then SEND ALL LINES.\r\n\r\n"
                    + "CONFIRMED ON YOUR FCU\r\nH234   heading 234\r\nS123   speed 123\r\nA12300 altitude 12300\r\nV900   vertical speed +900\r\nV-700  vertical speed -700\r\n\r\n"
                    + "C starts receiving (Initialize button sends the reference sequence).\r\n\r\n"
                    + "Any other ASCII command can be entered, but undocumented commands are NOT assumed to work. No firmware flashing. Simulator control is paused in mapping tools; connect the Caravan from Start.\r\n\r\n"
                    + "INLINE MAPPING (ON BY DEFAULT)\r\n1. Touch a control.\r\n2. Its description cell in the big list focuses automatically.\r\n3. Type its name. TAB saves. F8 pauses focus-following.\r\n\r\n"
                    + "Works for known and unknown inputs. Repeated events for the same control preserve your text and caret. A different control saves your edit and focuses its row. No automatic popup.\r\n\r\n"
                    + "Double-click an input row to name an earlier event. Event values are separate, so changing altitude does not create new unknown controls.\r\n\r\n"
                    + "For selector positions encoded in a value, check Only this exact value. For knob rotations, leave it unchecked.\r\n\r\n"
                    + "Correct labels directly in the grid's Observed mapping column.\r\n\r\n"
                    + "All RX/TX: captures\\*.jsonl\r\nMappings: mappings.json\r\nLearnings: "
                    + Path.GetFullPath(Path.Combine(folder, "..", "minicockpit.md"))
            };
            layout.Controls.Add(help, 1, 1);
            return layout;
        }

        private void ArmLearning(string label)
        {
            if (!IsConnected || initializeAt.HasValue)
                throw new InvalidOperationException("Connect and allow initialization to finish before learning.");
            if (String.IsNullOrWhiteSpace(label))
                throw new ArgumentException("Name the control before arming input learning.");
            learnLabel = label.Trim();
            learnInput.Text = learnLabel;
            learnButton.Text = "WAITING FOR INPUT...";
            Mark("LEARN: " + learnLabel);
        }

        private void NameInput(InputFrame frame)
        {
            if (frame == null) throw new InvalidOperationException("Touch a control first.");
            using (var dialog = new MappingDialog(frame, previousPanel, frame.FindMapping(mappings)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string existing;
                if (mappings.TryGetValue(dialog.MappingKey, out existing)
                    && !String.IsNullOrWhiteSpace(existing) && existing != dialog.MappingLabel
                    && MessageBox.Show(this, "Replace the existing label?\r\n\r\n" + existing,
                        "Confirm mapping change", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                previousPanel = dialog.PanelName;
                SaveMapping(dialog.MappingKey, dialog.MappingLabel, "touch-first naming");
                lastError = null;
            }
        }

        private void SaveMapping(string code, string label, string source)
        {
            if (String.IsNullOrWhiteSpace(code) || String.IsNullOrWhiteSpace(label))
                throw new ArgumentException("A mapping needs an event code and a non-empty description.");
            label = label.Trim();
            var updated = new Dictionary<string, string>(mappings);
            updated[code] = label;
            AtomicWrite(Path.Combine(folder, "mappings.json"), json.Serialize(updated));
            mappings = updated;
            InputFrame saved = InputFrame.Parse(code);
            if (counters.ContainsKey(saved.Code))
            {
                Counter counter = counters[saved.Code];
                counter.Row.Cells["mapping"].Value = (counter.LastFrame ?? saved).FindMapping(mappings) ?? "";
                counter.Row.DefaultCellStyle.BackColor = String.IsNullOrWhiteSpace(Convert.ToString(counter.Row.Cells["mapping"].Value)) ? Color.LightYellow : Color.White;
            }
            WriteLog("mapping", new { code = code, label = label, source = source });
            string notes = Path.GetFullPath(Path.Combine(folder, "..", "minicockpit.md"));
            File.AppendAllText(notes, "\r\n- " + DateTime.UtcNow.ToString("o") + ": token `" + code
                + ";` labeled **" + label + "** in the lab (" + source + "; user-assigned observation).\r\n");
            Append("SAVED MAPPING " + code + "; = " + label);
            if (lastFrame != null) UpdateLastInput();
        }

        private static void ValidateCommand(string text)
        {
            if (String.IsNullOrEmpty(text) || text.Length > 128 || text.Any(c => c > 127 || c == '\0'))
                throw new ArgumentException("Send 1-128 ASCII characters, without NUL.");
        }

        private void StartBatch(string text)
        {
            if (!IsConnected) throw new InvalidOperationException("Connect before sending a batch.");
            if (batchCommands.Count > 0) throw new InvalidOperationException("Stop the running batch first.");
            string ending = suffix.SelectedIndex == 1 ? ";" : suffix.SelectedIndex == 2 ? "\r\n" : "";
            string[] lines = text.Replace("\r\n", "\n").Split('\n').Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x + ending).ToArray();
            if (lines.Length == 0) throw new ArgumentException("Enter at least one command.");
            foreach (string line in lines) ValidateCommand(line);
            foreach (string line in lines) batchCommands.Enqueue(line);
            nextBatchAt = DateTime.UtcNow;
            WriteLog("batch-start", new { commands = lines, delayMs = batchGap.Value });
            Append("BATCH " + lines.Length + " commands queued.");
        }

        private void RefreshPorts()
        {
            string selected = Convert.ToString(ports.SelectedItem);
            ports.Items.Clear();
            foreach (string name in SerialPort.GetPortNames().OrderBy(x => x)) ports.Items.Add(name);
            if (ports.Items.Contains(selected)) ports.SelectedItem = selected;
            else if (ports.Items.Contains("COM5")) ports.SelectedItem = "COM5";
            else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
        }

        private void Connect()
        {
            if (IsConnected) throw new InvalidOperationException("Already connected.");
            string name = Convert.ToString(ports.SelectedItem);
            if (String.IsNullOrEmpty(name)) throw new InvalidOperationException("Choose a serial port.");
            serial = new SerialPort(name, Int32.Parse(Convert.ToString(baud.SelectedItem)), Parity.None, 8, StopBits.One);
            serial.Handshake = Handshake.None;
            serial.Encoding = Encoding.ASCII;
            serial.DtrEnable = false;
            serial.RtsEnable = false;
            serial.WriteTimeout = 1000;
            serial.ReadTimeout = 100;
            serial.ReadBufferSize = 65536;
            try { serial.Open(); }
            catch { serial.Dispose(); serial = null; throw; }
            parser.Reset();
            panelConnections.Reset();
            simulatorOutputs.Clear();
            lastError = null;
            initializeAt = autoInitialize.Checked ? (DateTime?)DateTime.UtcNow.AddSeconds(3) : null;
            automaticMappingReadyAt = DateTime.UtcNow.AddSeconds(autoInitialize.Checked ? 6 : 1);
            connect.Text = "Disconnect";
            ports.Enabled = baud.Enabled = false;
            WriteLog("connected", new { port = name, baud = serial.BaudRate, dataBits = 8, parity = "None", stopBits = 1, dtr = false, rts = false });
            Append("OPEN " + name + " " + serial.BaudRate + " 8N1. Initialization in 3 seconds if enabled.");
            UpdateStartup();
        }

        private void Disconnect()
        {
            lightMapping.PauseSweep();
            lightMapping.PauseQuickTest();
            simulatorOutputs.Clear();
            caravan.Reset();
            panelConnections.Reset();
            initializeAt = null;
            batchCommands.Clear();
            learnLabel = null;
            learnButton.Text = "LEARN NEXT INPUT";
            nameLastButton.Enabled = lastFrame != null;
            if (serial != null)
            {
                try { serial.Dispose(); }
                finally { serial = null; }
                WriteLog("disconnected", new { });
                Append("DISCONNECTED. DLS can use the port again.");
            }
            connect.Text = "Connect";
            ports.Enabled = baud.Enabled = true;
            UpdateStartup();
            if (!closing) SaveStatus();
        }

        private void InitializePanel()
        {
            // Reproduce the public reference handshake, without changing firmware.
            Send("C;");
            Send("C");
            Send("C");
            // Initialize the standalone EFIS to numeric QNH, 1013 hPa.
            Send("{1,");
            Send("@1,");
            Send("#1013,");
            initializeAt = null;
            automaticMappingReadyAt = DateTime.UtcNow.AddSeconds(3);
        }

        private void Send(string text)
        {
            if (!IsConnected) throw new InvalidOperationException("Connect first; close DLS if the port is in use.");
            ValidateCommand(text);
            serial.Write(text);
            WriteLog("tx", new { text = text, hex = Hex(Encoding.ASCII.GetBytes(text)), trial = currentTrial });
            Append("TX " + json.Serialize(text));
        }

        private void Mark(string label)
        {
            if (String.IsNullOrWhiteSpace(label)) throw new ArgumentException("Give this capture step a label.");
            currentTrial = label.Trim();
            trialInput.Text = currentTrial;
            foreach (Counter value in counters.Values) value.TrialStart = value.Count;
            dirty = true;
            WriteLog("mark", new { label = currentTrial });
            Append("MARK " + currentTrial);
        }

        private void ReceiveToken(string raw)
        {
            InputFrame frame = InputFrame.Parse(raw);
            hardwareView.Observe(frame, frame.FindMapping(mappings));
            panelConnections.Observe(frame.Code);
            UpdateStartup();
            string code = frame.Code;
            Counter counter;
            if (!counters.TryGetValue(code, out counter))
            {
                string mapping = frame.FindMapping(mappings);
                int row = grid.Rows.Add(code, "", 0, 0, "", Baseline.Describe(code), mapping ?? "");
                counter = new Counter { Code = code, Row = grid.Rows[row] };
                counters.Add(code, counter);
                WriteLog("new-token", new { code = code, trial = currentTrial });
                Append("NEW TOKEN " + code + " - " + Baseline.Describe(code));
            }
            counter.Count++;
            counter.LastSeen = DateTime.UtcNow;
            counter.LastFrame = frame;
            counter.Row.Cells["value"].Value = frame.Value ?? "";
            lastFrame = frame;
            nameLastButton.Enabled = true;
            if (learnLabel != null)
            {
                string label = learnLabel;
                learnLabel = null;
                learnButton.Text = "LEARN NEXT INPUT";
                SaveMapping(code, label, "learn next input");
            }
            string observed = frame.FindMapping(mappings);
            DataGridViewCell description = counter.Row.Cells["mapping"];
            if (!grid.IsCurrentCellInEditMode || grid.CurrentCell != description)
                description.Value = observed ?? "";
            counter.Row.DefaultCellStyle.BackColor = observed == null ? Color.LightYellow : Color.White;
            UpdateLastInput();
            Append("INPUT " + raw + ";  " + (observed ?? Baseline.Describe(code)));
            if (automaticMapping.Checked && grid.Visible && !lightMapping.Visible && DateTime.UtcNow >= automaticMappingReadyAt)
                FocusDescription(counter);
            dirty = true;
            Run(delegate { HandleSimulatorInput(frame); });
        }

        private void UpdateLastInput()
        {
            lastInput.Text = "LAST INPUT: " + lastFrame.Code
                + (lastFrame.Value == null ? "" : "   VALUE: " + lastFrame.Value)
                + "   " + (lastFrame.FindMapping(mappings) ?? "(not mapped - type a description in the list)");
        }

        private bool FinishDescriptionEdit()
        {
            if (!grid.IsCurrentCellInEditMode) return true;
            descriptionSaveFailed = false;
            if (!grid.EndEdit() || descriptionSaveFailed)
            {
                grid.Focus();
                grid.BeginEdit(false);
                return false;
            }
            return true;
        }

        private void FocusDescription(Counter counter)
        {
            DataGridViewCell cell = counter.Row.Cells["mapping"];
            if (grid.CurrentCell == cell && grid.IsCurrentCellInEditMode) return;
            if (!FinishDescriptionEdit()) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            grid.CurrentCell = cell;
            grid.FirstDisplayedScrollingRowIndex = cell.RowIndex;
            grid.Focus();
            if (!grid.BeginEdit(true))
                throw new InvalidOperationException("Could not focus the input description for editing.");
            TextBox editor = grid.EditingControl as TextBox;
            if (editor != null)
            {
                editor.Focus();
                editor.SelectAll();
            }
        }

        private void Tick()
        {
            if (closing) return;
            Run(delegate
            {
                if (IsConnected)
                {
                    int available = serial.BytesToRead;
                    if (available > 0)
                    {
                        byte[] bytes = new byte[Math.Min(available, 8192)];
                        int count = serial.Read(bytes, 0, bytes.Length);
                        if (count != bytes.Length) Array.Resize(ref bytes, count);
                        string text = Encoding.ASCII.GetString(bytes);
                        bytesReceived += count;
                        lastReceive = DateTime.UtcNow;
                        WriteLog("rx", new { text = text, hex = Hex(bytes), trial = currentTrial });
                        if (showRaw.Checked) Append("RX " + json.Serialize(text));
                        parser.Feed(text, ReceiveToken);
                    }
                    if (initializeAt.HasValue && DateTime.UtcNow >= initializeAt.Value) InitializePanel();
                }
                HandleCommand();
                SyncSimulator();
                if (IsConnected && batchCommands.Count > 0 && DateTime.UtcNow >= nextBatchAt)
                {
                    Send(batchCommands.Peek());
                    batchCommands.Dequeue();
                    nextBatchAt = DateTime.UtcNow.AddMilliseconds((double)batchGap.Value);
                    if (batchCommands.Count == 0)
                    {
                        WriteLog("batch-complete", new { });
                        Append("BATCH COMPLETE - commands transmitted; verify physical displays.");
                    }
                }
                if (dirty)
                {
                    foreach (Counter value in counters.Values)
                    {
                        value.Row.Cells["total"].Value = value.Count;
                        value.Row.Cells["trial"].Value = value.Count - value.TrialStart;
                        value.Row.Cells["last"].Value = value.Count == 0 ? "" : value.LastSeen.ToLocalTime().ToString("HH:mm:ss.fff");
                    }
                    dirty = false;
                }
                status.Text = (IsConnected ? serial.PortName + " CONNECTED" : "DISCONNECTED")
                    + " | RX bytes: " + bytesReceived + " | Unique tokens: " + counters.Count
                    + " | " + (lastError ?? currentTrial);
                UpdateStartup();
                if (DateTime.UtcNow >= nextSnapshot)
                {
                    SaveStatus();
                    nextSnapshot = DateTime.UtcNow.AddSeconds(1);
                }
            });
        }

        private void HandleCommand()
        {
            string path = Path.Combine(folder, "command.json");
            if (!File.Exists(path)) return;
            string processing = Path.Combine(folder, "command.processing.json");
            File.Move(path, processing);
            string requestId = "";
            try
            {
                var message = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(processing));
                requestId = Convert.ToString(message["id"]);
                string action = Convert.ToString(message["action"]);
                string value = message.ContainsKey("value") ? Convert.ToString(message["value"]) : "";
                if (action == "send") Send(value);
                else if (action == "mark") Mark(value);
                else if (action == "disconnect") Disconnect();
                else if (action == "connect") Connect();
                else if (action == "learn") ArmLearning(value);
                else if (action == "map") SaveMapping(Convert.ToString(message["code"]), value, "explicit mapping");
                else if (action == "batch") StartBatch(value);
                else if (action == "cancel-batch") batchCommands.Clear();
                else if (action == "quick-test") ToggleQuickTest();
                else if (action == "aircraft") startup.SelectAircraft(value);
                else if (action == "sim-connect") ToggleSimulator();
                else if (action == "sim-input")
                {
                    if (!CanControlSimulator) throw new InvalidOperationException("Live Caravan control is not ready.");
                    InputFrame frame = InputFrame.Parse(value);
                    WriteLog("simulator-diagnostic-input", new { raw = value });
                    foreach (SimulatorCommand simCommand in caravan.Handle(frame, simulator.State)) simulator.Send(simCommand);
                }
                else throw new ArgumentException("Unknown action: " + action);
                AtomicWrite(Path.Combine(folder, "response.json"), json.Serialize(new { id = requestId, ok = true }));
            }
            catch (Exception exception)
            {
                AtomicWrite(Path.Combine(folder, "response.json"), json.Serialize(new { id = requestId, ok = false, error = exception.Message }));
                throw;
            }
            finally { File.Delete(processing); }
        }

        private void SaveStatus()
        {
            var snapshot = new
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                pid = Process.GetCurrentProcess().Id,
                connected = IsConnected,
                port = IsConnected ? serial.PortName : null,
                baud = IsConnected ? serial.BaudRate : 0,
                bytesReceived = bytesReceived,
                lastReceiveUtc = lastReceive.HasValue ? lastReceive.Value.ToString("o") : null,
                trial = currentTrial,
                capture = sessionPath,
                error = lastError,
                learning = learnLabel,
                batchPending = batchCommands.Count,
                automaticMapping = automaticMapping.Checked,
                panels = new { fcu = IsConnected && panelConnections.Fcu, efis = IsConnected && panelConnections.Efis, overhead = IsConnected && panelConnections.Overhead },
                quickTest = lightMapping.QuickTestRunning,
                aircraft = new { automatic = startup.AutomaticSelection, selected = startup.SelectedAircraft },
                simulator = new { connected = simulator.Connected, fresh = simulator.Fresh, status = simulator.Status,
                    controlEnabled = CanControlSimulator, state = simulator.State },
                focusedDescription = grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == grid.Columns["mapping"].Index
                    ? Convert.ToString(grid.CurrentCell.OwningRow.Cells["code"].Value) : null,
                lastInput = lastFrame == null ? null : new { code = lastFrame.Code, value = lastFrame.Value, raw = lastFrame.Raw },
                events = counters.Values.Select(c => new { code = c.Code, value = c.LastFrame == null ? null : c.LastFrame.Value, total = c.Count, sinceMark = c.Count - c.TrialStart, reference = Baseline.Describe(c.Code), observed = (c.LastFrame ?? InputFrame.Parse(c.Code)).FindMapping(mappings) }).ToArray()
            };
            AtomicWrite(Path.Combine(folder, "status.json"), json.Serialize(snapshot));
        }

        private static void AtomicWrite(string path, string text)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private void WriteLog(string type, object data)
        {
            capture.WriteLine(json.Serialize(new { utc = DateTime.UtcNow.ToString("o"), type = type, data = data }));
        }

        private void UpdateStartup()
        {
            startup.UpdateConnection(IsConnected, IsConnected ? serial.PortName : null, panelConnections, lastError);
            string message = simulator.Connected && !simulator.Fresh ? "MSFS telemetry unavailable / controls paused"
                : simulator.Status + (ToolsVisible && simulator.Connected ? " / controls paused in mapping tools" : "");
            startup.UpdateSimulator(simulator.Connected || simulator.Connecting, message, lightMapping.QuickTestRunning, IsConnected && !initializeAt.HasValue);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == SimulatorBridge.WindowMessage && simulator != null)
                Run(simulator.ReceiveMessage);
            base.WndProc(ref message);
        }

        private void Append(string text)
        {
            if (logView.TextLength > 100000)
                logView.Text = logView.Text.Substring(logView.TextLength - 50000);
            logView.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
        }

        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", " "); }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.F8)
            {
                automaticMapping.Checked = !automaticMapping.Checked;
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                if (!(exception is IOException || exception is UnauthorizedAccessException
                    || exception is FormatException || exception is InvalidOperationException
                    || exception is ArgumentException))
                    throw;
                lastError = exception.Message;
                WriteLog("error", new { message = exception.ToString() });
                Append("ERROR " + exception.Message);
                if (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
                {
                    try { Disconnect(); }
                    catch (Exception disconnectError)
                    {
                        WriteLog("error", new { message = "Disconnect failed: " + disconnectError });
                        Append("ERROR disconnect: " + disconnectError.Message);
                    }
                }
                status.Text = "ERROR: " + exception.Message;
                UpdateStartup();
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            try { Application.Run(new LabWindow(args.Contains("--connect"))); }
            catch (Exception exception)
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal.log"), DateTime.UtcNow.ToString("o") + " " + exception + Environment.NewLine);
                MessageBox.Show(exception.ToString(), "miniCOCKPIT Lab error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }
    }
}
