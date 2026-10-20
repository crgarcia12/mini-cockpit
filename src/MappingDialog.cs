using System;
using System.Drawing;
using System.Windows.Forms;

namespace MiniCockpitLab
{
    internal sealed class MappingDialog : Form
    {
        private readonly ComboBox panel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox control = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox action = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly CheckBox exact = new CheckBox { AutoSize = true, Text = "Only this exact value (switch position, not a rotary value)" };
        private readonly InputFrame frame;

        public string MappingKey { get; private set; }
        public string MappingLabel { get; private set; }
        public string PanelName { get { return panel.Text; } }

        public MappingDialog(InputFrame captured, string previousPanel, string existingLabel)
        {
            frame = captured;
            Text = "Name the control you just touched";
            ClientSize = new Size(650, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            for (int i = 1; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(layout);
            var capturedLabel = new Label
            {
                AutoSize = false, Dock = DockStyle.Fill,
                Text = "Captured: " + frame.Raw + ";     Code: " + frame.Code
                    + (frame.Value == null ? "" : "     Value: " + frame.Value)
                    + "\r\nThis input is frozen while you name it; later movements will not change it."
            };
            layout.Controls.Add(capturedLabel, 0, 0);
            layout.SetColumnSpan(capturedLabel, 2);
            panel.Items.AddRange(new object[] { "FCU", "EFIS", "Overhead", "Other" });
            panel.Text = previousPanel;
            control.Items.AddRange(new object[] { "Heading knob", "Altitude knob", "Speed knob", "V/S knob", "BARO knob", "FD", "APU MASTER", "APU START", "SEAT BELTS", "ALT increment selector" });
            action.Items.AddRange(new object[] { "Clockwise", "Counterclockwise", "Push", "Pull", "Press", "Release", "OFF -> ON", "ON -> OFF", "100", "1000" });
            AddField(layout, 1, "Panel", panel);
            AddField(layout, 2, "Control", control);
            AddField(layout, 3, "Action", action);
            exact.Enabled = frame.Value != null;
            layout.Controls.Add(exact, 1, 4);
            var current = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Current label: " + (existingLabel ?? "(not mapped)") };
            layout.Controls.Add(current, 0, 5);
            layout.SetColumnSpan(current, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "SAVE MAPPING", AutoSize = true };
            save.Click += delegate
            {
                if (String.IsNullOrWhiteSpace(panel.Text) || String.IsNullOrWhiteSpace(control.Text) || String.IsNullOrWhiteSpace(action.Text))
                {
                    MessageBox.Show(this, "Choose or type a panel, control, and action.", "Mapping needs a name", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                MappingKey = exact.Checked ? frame.Raw : frame.Code;
                MappingLabel = panel.Text.Trim() + " / " + control.Text.Trim() + " / " + action.Text.Trim();
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 6);
            layout.SetColumnSpan(buttons, 2);
            AcceptButton = save;
            CancelButton = cancel;
            Shown += delegate { control.Focus(); };
        }

        private static void AddField(TableLayoutPanel layout, int row, string label, Control field)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, row);
            layout.Controls.Add(field, 1, row);
        }
    }
}
