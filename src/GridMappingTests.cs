using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MiniCockpitLab
{
    internal static class GridMappingTests
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr key, IntPtr flags);

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--sweep-only") >= 0)
            {
                TestOutputSweep();
                return;
            }
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var receive = typeof(LabWindow).GetMethod("ReceiveToken", flags);
            string mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings.json");
            var json = new JavaScriptSerializer();
            using (var window = new LabWindow(false, false))
            {
                window.Show();
                Application.DoEvents();
                var navigation = (TabControl)typeof(LabWindow).GetField("navigation", flags).GetValue(window);
                var startup = (StartupPanel)typeof(LabWindow).GetField("startup", flags).GetValue(window);
                var panels = (PanelConnections)typeof(LabWindow).GetField("panelConnections", flags).GetValue(window);
                var grid = (DataGridView)typeof(LabWindow).GetField("grid", flags).GetValue(window);
                Check(navigation.SelectedIndex == 0 && navigation.TabPages[0].Text == "Start"
                    && navigation.TabPages[1].Text == "Test", "Start must be the default, with the tools in Test.");
                startup.SelectAircraft("Airbus A320neo");
                startup.SelectAircraft(AircraftProfiles.Automatic);
                startup.UpdateDetectedAircraft("C208B Cargo");
                Check(startup.AutomaticSelection && startup.CaravanSelected
                    && File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aircraft.txt")) == AircraftProfiles.Automatic,
                    "Automatic detection must resolve the profile without replacing the saved Auto preference.");
                startup.SelectAircraft("Airbus A320neo");
                startup.UpdateDetectedAircraft("Cessna Grand Caravan EX");
                Check(!startup.AutomaticSelection && !startup.CaravanSelected && startup.SelectedAircraft == "Airbus A320neo",
                    "Simulator telemetry must not overwrite a deliberate manual choice.");
                startup.SelectAircraft(AircraftProfiles.Automatic);
                Check(startup.CaravanSelected, "Returning to Auto must immediately use the latest detected aircraft.");
                startup.UpdateDetectedAircraft("Cessna 172");
                Check(startup.SelectedAircraft == null && !startup.CaravanSelected,
                    "An unrecognized aircraft must clear the automatic profile, not retain Caravan control.");
                startup.UpdateDetectedAircraft("Airbus A321 Neo");
                Check(startup.SelectedAircraft == "Airbus A321neo", "Auto must follow an aircraft change.");
                startup.SelectAircraft(AircraftProfiles.Caravan);
                Check(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aircraft.txt")) == "Cessna 208B Grand Caravan",
                    "Aircraft selection must persist outside the mapping files.");
                receive.Invoke(window, new object[] { "956" });
                receive.Invoke(window, new object[] { "17,12300" });
                Check(panels.Efis && !grid.IsCurrentCellInEditMode && startup.Visible && !grid.Visible,
                    "Startup must track hardware without starting a hidden edit or stealing focus.");
                navigation.SelectedIndex = 1;
                Application.DoEvents();
                var testWorkspace = (TabControl)typeof(LabWindow).GetField("testWorkspace", flags).GetValue(window);
                var hardware = (HardwarePanel)typeof(LabWindow).GetField("hardwareView", flags).GetValue(window);
                receive.Invoke(window, new object[] { "50" });
                Check(hardware.Visible && hardware.LastControlId == "ap1" && !grid.IsCurrentCellInEditMode,
                    "Test must open on the hardware photographs and highlight AP1 without focusing a hidden editor.");
                receive.Invoke(window, new object[] { "101,1014" });
                Check(hardware.LastControlId == "qnh", "BARO value frames must highlight the actual pressure knob.");
                receive.Invoke(window, new object[] { "207" });
                Check(hardware.LastControlId == "apustart", "Overhead inputs must highlight their own physical control.");
                testWorkspace.SelectedIndex = 1;
                Application.DoEvents();
                receive.Invoke(window, new object[] { "17,12300" });
                Application.DoEvents();
                Check(grid.CurrentCell.OwningColumn.Name == "mapping" && Convert.ToString(grid.CurrentRow.Cells["code"].Value) == "17",
                    "Hardware input must select its own description cell.");
                var editor = (TextBox)grid.EditingControl;
                Check(editor.Focused, "The description must focus without clicking.");
                editor.Text = "TEST altitude clockwise";
                editor.SelectionStart = 4;
                receive.Invoke(window, new object[] { "17,12500" });
                Check(editor.Text == "TEST altitude clockwise" && editor.SelectionStart == 4,
                    "Repeated values must not overwrite typing or reset the caret.");
                receive.Invoke(window, new object[] { "62" });
                Check(Convert.ToString(grid.CurrentRow.Cells["code"].Value) == "62" && grid.IsCurrentCellInEditMode,
                    "A different input must focus its description.");
                var saved = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
                Check(saved["17"] == "TEST altitude clockwise", "Changing inputs must persist the previous description.");
                editor = (TextBox)grid.EditingControl;
                editor.Text = "TEST FD press";
                Check(PostMessage(editor.Handle, 0x100, (IntPtr)9, (IntPtr)1), "Could not deliver TAB.");
                PostMessage(editor.Handle, 0x101, (IntPtr)9, (IntPtr)1);
                Application.DoEvents();
                saved = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
                Check(saved["62"] == "TEST FD press", "TAB must persist the inline description.");
                receive.Invoke(window, new object[] { "17,12600" });
                editor = (TextBox)grid.EditingControl;
                Check(editor.Text == "TEST altitude clockwise" && editor.SelectionLength == editor.TextLength,
                    "Known controls must also focus with the existing description selected.");
                editor.Text = "TEST retry after write failure";
                string blockedTemporary = mappingPath + ".tmp";
                Directory.CreateDirectory(blockedTemporary);
                try
                {
                    receive.Invoke(window, new object[] { "63" });
                    Check(Convert.ToString(grid.CurrentRow.Cells["code"].Value) == "17"
                        && ((TextBox)grid.EditingControl).Text == "TEST retry after write failure",
                        "A failed save must retain the active row and typed description.");
                }
                finally { Directory.Delete(blockedTemporary); }
                receive.Invoke(window, new object[] { "62" });
                saved = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
                Check(saved["17"] == "TEST retry after write failure", "Retry must persist the description after an IO failure.");
                receive.Invoke(window, new object[] { "17,12700" });
                editor = (TextBox)grid.EditingControl;
                editor.Text = "";
                receive.Invoke(window, new object[] { "63" });
                saved = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
                Check(saved["17"] == "TEST retry after write failure", "Blank edits must not erase existing descriptions.");
                ((TextBox)grid.EditingControl).Text = "TEST save on leaving Test";
                navigation.SelectedIndex = 0;
                Application.DoEvents();
                saved = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
                Check(saved["63"] == "TEST save on leaving Test" && !grid.IsCurrentCellInEditMode,
                    "Returning to Start must persist the active description.");
                receive.Invoke(window, new object[] { "959" });
                Check(!grid.IsCurrentCellInEditMode && startup.Visible, "Hardware on Start must not return to Test.");
                navigation.SelectedIndex = 1;
                Application.DoEvents();
                receive.Invoke(window, new object[] { "63" });
                ((TextBox)grid.EditingControl).Text = "TEST save on close";
                window.Close();
                saved = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(mappingPath));
                Check(saved["63"] == "TEST save on close", "Closing the window must save the active edit.");
            }
            using (var reopened = new LabWindow(false, false))
            {
                var startup = (StartupPanel)typeof(LabWindow).GetField("startup", flags).GetValue(reopened);
                var aircraft = (ComboBox)typeof(StartupPanel).GetField("aircraft", flags).GetValue(startup);
                Check(Convert.ToString(aircraft.SelectedItem) == "Cessna 208B Grand Caravan",
                    "Reopening the app must restore the selected aircraft.");
                reopened.Show();
                Application.DoEvents();
                reopened.Close();
            }
            Console.WriteLine("Grid mapping tests passed: direct autofocus, repeated-value typing, switch-and-save, real TAB messages, known controls, empty edits, and save-on-close.");
            TestHardwareHighlights();
            TestLightMapping();
            TestQuickTest();
            TestOutputSweep();
        }

        private static void TestHardwareHighlights()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (var form = new Form { ClientSize = new Size(1180, 680) })
            using (var panel = new HardwarePanel(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets")))
            {
                form.Controls.Add(panel);
                form.Show();
                Application.DoEvents();
                var areas = (Dictionary<string, RectangleF>)typeof(HardwarePanel).GetField("hitAreas", flags).GetValue(panel);
                Check(areas["qnh"].Right < areas["speed"].Left && areas["verticalspeed"].Right < areas["apustart"].Left,
                    "Hardware must be arranged EFIS left, FCU center, and overhead right.");
                Rectangle ap = Rectangle.Ceiling(areas["ap1"]);
                Rectangle heading = Rectangle.Ceiling(areas["heading"]);
                Rectangle apGlow = Rectangle.Inflate(ap, 7, 7);
                Rectangle headingGlow = Rectangle.Inflate(heading, 7, 7);
                Func<SimulatorState> telemetry = () => new SimulatorState
                {
                    Aircraft = "C208B Cargo", Autopilot = true, NavigationHold = true, HeadingHold = true,
                    AltitudeHold = true, Strobe = true, Beacon = true, Navigation = true
                };
                using (var offline = Render(panel))
                {
                    panel.SetSimulatorState(telemetry());
                    using (var baseline = Render(panel))
                    {
                        Check(ChangedPixels(offline, baseline, apGlow, ap) == 0
                            && ChangedPixels(offline, baseline, headingGlow, Rectangle.Empty) == 0,
                            "Active simulator modes alone must not draw green control rectangles.");
                        Check(ChangedPixels(offline, baseline, ap, Rectangle.Empty) > 0,
                            "The actual AP1 indicator must still reflect the simulator.");
                        panel.Observe(InputFrame.Parse("50"), "");
                        panel.Observe(InputFrame.Parse("3"), "");
                        var clock = Stopwatch.StartNew();
                        using (var active = Render(panel))
                            Check(ChangedPixels(baseline, active, apGlow, Rectangle.Empty) > 50
                                && ChangedPixels(baseline, active, headingGlow, Rectangle.Empty) > 50,
                                "Physical button and knob input must draw their own activity rectangles.");
                        Action poll = delegate
                        {
                            panel.SetSimulatorState(telemetry());
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(10);
                        };
                        while (clock.ElapsedMilliseconds < 1100) poll();
                        panel.Observe(InputFrame.Parse("3"), "");
                        while (clock.ElapsedMilliseconds < 2250) poll();
                        using (var repeated = Render(panel))
                            Check(ChangedPixels(baseline, repeated, apGlow, Rectangle.Empty) == 0
                                && ChangedPixels(baseline, repeated, headingGlow, Rectangle.Empty) > 50,
                                "Only new physical input may extend a pulse; telemetry must not extend AP1's rectangle.");
                        while (clock.ElapsedMilliseconds < 3300) poll();
                        using (var expired = Render(panel))
                            Check(ChangedPixels(baseline, expired, apGlow, Rectangle.Empty) == 0
                                && ChangedPixels(baseline, expired, headingGlow, Rectangle.Empty) == 0,
                                "Expired rectangles must disappear while actual simulator indicators remain lit.");
                    }
                }
                form.Close();
            }
            Console.WriteLine("Hardware tests passed: panel arrangement, simulator LEDs, refreshed input pulses, and expiry during live telemetry.");
        }

        private static Bitmap Render(Control control)
        {
            var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
            control.DrawToBitmap(bitmap, control.ClientRectangle);
            return bitmap;
        }

        private static int ChangedPixels(Bitmap first, Bitmap second, Rectangle bounds, Rectangle exclude)
        {
            int changed = 0;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
                for (int x = bounds.Left; x < bounds.Right; x++)
                    if (!exclude.Contains(x, y) && first.GetPixel(x, y) != second.GetPixel(x, y)) changed++;
            return changed;
        }

        private static void TestQuickTest()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quick-test-light-mappings.json");
            var sent = new List<string>();
            using (var panel = new LightMappingPanel(path, sent.Add, action => action(), message => { }))
            {
                panel.StartQuickTest();
                Check(panel.QuickTestRunning && panel.IsTesting && sent.Count == 58
                    && sent[29] == "\\161," && sent[57] == "!1,",
                    "Quick test must light all 29 annunciators simultaneously, without mutually exclusive LCD tests.");
                var clock = Stopwatch.StartNew();
                while (panel.QuickTestRunning && clock.ElapsedMilliseconds < 4500)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }
                Check(!panel.QuickTestRunning && !panel.IsTesting && sent.Count == 87 && sent[86] == "!0,",
                    "Quick test must automatically extinguish every annunciator after three seconds.");
                Check(!File.Exists(path), "Quick test must never create or change light mappings.");
                panel.StartQuickTest();
                int before = sent.Count;
                panel.Stop();
                Check(!panel.QuickTestRunning && sent.Count == before + 29, "Stopping must cancel Quick test and switch every light off.");
                panel.StartQuickTest();
                before = sent.Count;
                panel.PauseQuickTest();
                Check(!panel.QuickTestRunning && sent.Count == before, "Disconnect must cancel the timer without attempting writes to a closed port.");
            }
            Console.WriteLine("Quick test passed: all 29 lights, automatic cleanup, manual cancellation, disconnect, and mapping preservation.");
        }

        private static void TestLightMapping()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-light-mappings.json");
            File.Delete(path);
            var sent = new List<string>();
            var json = new JavaScriptSerializer();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (var host = new Form())
            using (var panel = new LightMappingPanel(path, sent.Add, action => action(), message => { }))
            {
                host.Controls.Add(panel);
                host.Show();
                Application.DoEvents();
                panel.Start();
                var editor = (TextBox)typeof(LightMappingPanel).GetField("description", flags).GetValue(panel);
                Check(editor.Focused && sent.Count == 30 && sent[29] == "\\161,",
                    "Start must clear all 29 channels, illuminate the first overhead channel, and focus its description.");
                editor.Text = "TEST apu.master.on";
                Check(PostMessage(editor.Handle, 0x100, (IntPtr)9, (IntPtr)1), "Could not deliver light-mapping TAB.");
                PostMessage(editor.Handle, 0x101, (IntPtr)9, (IntPtr)1);
                Application.DoEvents();
                var saved = json.Deserialize<Dictionary<string, LightMapping>>(File.ReadAllText(path));
                Check(saved["overhead.16"].Description == "TEST apu.master.on"
                    && saved["overhead.16"].OnCommand == "\\161," && saved["overhead.16"].OffCommand == "\\160,",
                    "TAB must save the name and exact replay commands.");
                Check(File.ReadAllText(path) == File.ReadAllText(Path.GetFullPath(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "minicockpit-light-mappings.json"))),
                    "Each saved light mapping must have a matching backup outside its application directory.");
                Check(sent[sent.Count - 2] == "\\160," && sent[sent.Count - 1] == "\\241," && editor.Focused,
                    "TAB must switch the previous light off before illuminating the next and retain typing focus.");
                panel.Activate(0);
                Check(editor.Text == "TEST apu.master.on" && editor.SelectionLength == editor.TextLength,
                    "Retesting a saved channel must prefill and select its description.");
                editor.Text = "";
                panel.Next();
                saved = json.Deserialize<Dictionary<string, LightMapping>>(File.ReadAllText(path));
                Check(saved["overhead.16"].Description == "TEST apu.master.on", "Blank names must not erase saved lights.");
                editor.Text = "TEST apu.master.fault";
                Directory.CreateDirectory(path + ".tmp");
                int previousCount = sent.Count;
                bool rejected = false;
                try { panel.Next(); }
                catch (UnauthorizedAccessException) { rejected = true; }
                catch (IOException) { rejected = true; }
                finally { Directory.Delete(path + ".tmp"); }
                Check(rejected && sent.Count == previousCount && editor.Text == "TEST apu.master.fault",
                    "Failed persistence must retain the current light and draft without advancing.");
                panel.Next();
                saved = json.Deserialize<Dictionary<string, LightMapping>>(File.ReadAllText(path));
                Check(saved["overhead.24"].Description == "TEST apu.master.fault", "A retry must persist the draft.");
                editor.Text = "TEST save on stop";
                panel.Stop();
                saved = json.Deserialize<Dictionary<string, LightMapping>>(File.ReadAllText(path));
                Check(saved["overhead.17"].Description == "TEST save on stop" && sent[sent.Count - 1] == "\\170,",
                    "Stop must save the draft and extinguish the active light.");
                panel.AllOff();
                Check(sent[sent.Count - 1] == "!0,", "All-off must include EFIS channels.");
                host.Close();
            }
            using (var host = new Form())
            using (var panel = new LightMappingPanel(path, sent.Add, action => action(), message => { }))
            {
                host.Controls.Add(panel);
                host.Show();
                panel.Activate(0);
                var editor = (TextBox)typeof(LightMappingPanel).GetField("description", flags).GetValue(panel);
                Check(editor.Text == "TEST apu.master.on", "Saved light mappings must survive reopening.");
                panel.Activate(FindLight(panel, "screen.efis.qnh"));
                Check(sent[sent.Count - 3] == "{1," && sent[sent.Count - 2] == "@1," && sent[sent.Count - 1] == "#1013,",
                    "A QNH screen test must send its initialization and numeric value as separate framed writes.");
                editor.Text = "TEST QNH legend";
                panel.Next();
                var saved = json.Deserialize<Dictionary<string, LightMapping>>(File.ReadAllText(path));
                Check(saved["screen.efis.qnh"].Description == "TEST QNH legend"
                    && saved["screen.efis.qnh"].OnCommand == "{1,\n@1,\n#1013,",
                    "Screen mappings must persist the complete ordered test sequence.");
                Check(sent[sent.Count - 3] == "{1," && sent[sent.Count - 2] == "@0," && sent[sent.Count - 1] == "#1013,",
                    "Advancing must activate the QFE test, not leave the QNH mode active.");
                panel.Activate(FindLight(panel, "screen.fcu.speed-dot"));
                Check(sent[sent.Count - 1] == "z,", "The speed dot must use its recovered command.");
                panel.Stop();
                Check(sent[sent.Count - 1] == "x,", "Stopping the dot test must clear the dot.");
                panel.Activate(FindLight(panel, "screen.fcu.vs-negative"));
                Check(sent[sent.Count - 2] == "W," && sent[sent.Count - 1] == "V-700,",
                    "V/S testing must include a negative number for the minus sign.");
                int beforeReset = sent.Count;
                panel.AllOff();
                Check(sent.Count - beforeReset == 31 && sent[beforeReset] == "W," && sent[beforeReset + 1] == "V0,"
                    && sent[sent.Count - 1] == "!0,",
                    "All-off must restore only the active LCD test and clear the 29 annunciators, not replay every LCD state.");
                var grid = (DataGridView)typeof(LightMappingPanel).GetField("grid", flags).GetValue(panel);
                panel.Activate(grid.Rows.Count - 1);
                editor.Text = "TEST final light";
                panel.Next();
                Check(editor.ReadOnly && sent[sent.Count - 1] == "o,", "Finishing must restore the final output without wrapping.");
                host.Close();
            }
            Console.WriteLine("Light mapping tests passed: exact commands, one-at-a-time TAB workflow, blank preservation, write-failure retry, stop, all-off, reload, and completion.");
        }

        private static int FindLight(LightMappingPanel panel, string id)
        {
            var grid = (DataGridView)typeof(LightMappingPanel).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            foreach (DataGridViewRow row in grid.Rows)
                if (Convert.ToString(row.Cells["channel"].Value) == id) return row.Index;
            throw new Exception("Output channel not found: " + id);
        }

        private static void TestOutputSweep()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-sweep-mappings.json");
            File.Delete(path);
            var sent = new List<string>();
            var times = new List<double>();
            var clock = Stopwatch.StartNew();
            bool finished = false;
            using (var host = new Form { ShowInTaskbar = false, Opacity = 0 })
            using (var panel = new LightMappingPanel(path, command =>
            {
                sent.Add(command);
                times.Add(clock.Elapsed.TotalMilliseconds);
            }, action => action(), message => { if (message == "OUTPUT SWEEP COMPLETE") finished = true; }))
            {
                host.Controls.Add(panel);
                host.Show();
                var playButton = (Button)typeof(LightMappingPanel).GetField("playAll",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                playButton.PerformClick();
                host.Hide();
                Application.DoEvents();
                Check(playButton.Text == "STOP", "PLAY ALL must become STOP while playback runs.");
                PumpUntil(() => finished, 20000, () => "Full sweep: " + sent.Count + " writes; last: "
                    + (sent.Count == 0 ? "(none)" : sent[sent.Count - 1]));
                var catalog = (List<LightChannel>)typeof(LightMappingPanel).GetField("channels",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                var expected = new List<string>();
                foreach (var channel in catalog)
                    if (!channel.IsScreen) expected.AddRange(channel.OffCommand.Split('\n'));
                var stepStarts = new List<int>();
                for (int pass = 0; pass < 2; pass++)
                    foreach (var channel in catalog)
                    {
                        stepStarts.Add(expected.Count);
                        expected.AddRange((pass == 0 ? channel.OnCommand : channel.OffCommand).Split('\n'));
                    }
                Check(String.Join("|", sent) == String.Join("|", expected),
                    "The sweep must run every ON sequence before every OFF sequence, preserving LCD command groups.");
                for (int i = 1; i < stepStarts.Count; i++)
                    Check(times[stepStarts[i]] - times[stepStarts[i - 1]] >= 99,
                        "Sweep outputs must be spaced at 100 ms; observed gap at step " + i + ": "
                        + (times[stepStarts[i]] - times[stepStarts[i - 1]]) + " ms.");
                Check(!File.Exists(path), "A sweep must not create or change mappings.");
                Check(playButton.Text == "PLAY ALL (100 ms)", "Completion must restore the PLAY ALL button.");

                sent.Clear();
                times.Clear();
                host.Show();
                playButton.PerformClick();
                host.Hide();
                PumpUntil(() => sent.Count >= 31, 2000, () => "Cancellation setup: " + sent.Count + " writes");
                host.Show();
                playButton.PerformClick();
                host.Hide();
                Check(playButton.Text == "PLAY ALL (100 ms)", "Clicking STOP must cancel and restore PLAY ALL.");
                Check(sent[sent.Count - 2] == "\\160," && sent[sent.Count - 1] == "\\240,",
                    "STOP must switch off every output reached by a partial sweep.");
                int stoppedCount = sent.Count;
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                Check(sent.Count == stoppedCount, "A cancelled sweep must not emit more timer commands.");
                host.Close();
            }

            IOException error = null;
            sent.Clear();
            using (var host = new Form { ShowInTaskbar = false, Opacity = 0 })
            using (var panel = new LightMappingPanel(path, command =>
            {
                if (command == "\\241,") throw new IOException("TEST serial write failure");
                sent.Add(command);
            }, action =>
            {
                try { action(); }
                catch (IOException exception) { error = exception; }
            }, message => { }))
            {
                host.Controls.Add(panel);
                host.Show();
                host.Hide();
                panel.StartSweep();
                PumpUntil(() => error != null, 2000, () => "Failure setup: " + sent.Count + " writes");
                int failedCount = sent.Count;
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                Check(sent.Count == failedCount, "A serial failure must surface through the error handler and stop the sweep.");
                var playButton = (Button)typeof(LightMappingPanel).GetField("playAll",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                Check(playButton.Text == "PLAY ALL (100 ms)", "A playback error must restore the PLAY ALL button.");
                host.Close();
            }
            Console.WriteLine("Output sweep tests passed: all-on/all-off order, grouped LCD writes, real 100 ms timing, cancellation cleanup, and serial failure.");
        }

        private static void PumpUntil(Func<bool> condition, int timeoutMilliseconds, Func<string> diagnostic)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.ElapsedMilliseconds < timeoutMilliseconds)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
            Check(condition(), "Timed out waiting for the Windows Forms test. " + diagnostic());
        }
    }
}
