using System;
using System.Drawing;
using System.Windows.Forms;

namespace PeripheralCharge
{
    internal sealed class DetailsForm : Form
    {
        private readonly Label updated;
        private readonly Button refresh;
        private readonly ListView list;
        private readonly Label empty;

        public DetailsForm(Action onRefresh)
        {
            Text = "余电 · 外设电量";
            Font = new Font("Microsoft YaHei UI", 9);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(446, 241);
            MinimumSize = new Size(400, 235);
            using (Icon generated = BatteryIcon.Draw(null)) Icon = (Icon)generated.Clone();

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12),
                ColumnCount = 2, RowCount = 3, BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            var title = new Label { Text = "外设电量", Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
                AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(27, 51, 45) };
            layout.Controls.Add(title, 0, 0);
            refresh = new Button { Text = "立即刷新", AutoSize = true, FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Right, Padding = new Padding(3, 0, 3, 0) };
            refresh.Click += delegate { onRefresh(); };
            layout.Controls.Add(refresh, 1, 0);

            var body = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2, 8, 2, 0) };
            layout.Controls.Add(body, 0, 1);
            layout.SetColumnSpan(body, 2);
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, BorderStyle = BorderStyle.None,
                FullRowSelect = true, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                AccessibleName = "设备电量列表" };
            list.Columns.Add("设备", 172);
            list.Columns.Add("电量", 108);
            list.Columns.Add("接收器", 96);
            body.Controls.Add(list);
            empty = new Label { Text = "正在读取接收器…", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, BackColor = Color.White };
            body.Controls.Add(empty);
            empty.BringToFront();
            updated = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray };
            layout.Controls.Add(updated, 0, 2);
            layout.SetColumnSpan(updated, 2);
            list.SizeChanged += delegate {
                if (list.ClientSize.Width > list.Columns[1].Width + list.Columns[2].Width + 80)
                    list.Columns[0].Width = list.ClientSize.Width - list.Columns[1].Width - list.Columns[2].Width - SystemInformation.VerticalScrollBarWidth;
            };
        }

        public void Display(Snapshot snapshot)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (DeviceStatus d in snapshot.Devices)
                {
                    var item = new ListViewItem(new string[] { d.Name, d.Battery.Display, d.Id });
                    if (!d.Battery.Percent.HasValue) item.ForeColor = Color.DimGray;
                    else if (d.Battery.Percent <= 15) item.ForeColor = Color.Firebrick;
                    list.Items.Add(item);
                }
            }
            finally { list.EndUpdate(); }
            empty.Text = snapshot.EmptyMessage;
            empty.Visible = snapshot.Devices.Count == 0;
            updated.Text = "上次刷新 " + snapshot.Checked.ToString("HH:mm:ss") + "   ·   每 " + Tray.IntervalSeconds + " 秒更新";
        }

        public void SetBusy(bool busy)
        {
            refresh.Enabled = !busy;
            refresh.Text = busy ? "读取中…" : "立即刷新";
        }
    }
}
