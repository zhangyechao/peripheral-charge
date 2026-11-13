using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeripheralCharge
{
    internal static class BatteryIcon
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        public static Icon Draw(int? level)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                // A contrasting outline keeps the small gauge legible on either taskbar theme.
                using (var rim = new Pen(Color.FromArgb(30, 49, 55), 5))
                    g.DrawRectangle(rim, 3, 7, 23, 17);
                using (var rim = new Pen(Color.FromArgb(231, 243, 237), 2))
                    g.DrawRectangle(rim, 3, 7, 23, 17);
                g.FillRectangle(Brushes.DarkSlateGray, 27, 12, 4, 7);
                g.FillRectangle(Brushes.WhiteSmoke, 27, 13, 3, 5);
                g.FillRectangle(Brushes.DarkSlateGray, 5, 9, 19, 13);
                if (level.HasValue)
                {
                    Color color = level <= 15 ? Color.Salmon : Color.FromArgb(120, 233, 175);
                    using (var fill = new SolidBrush(color))
                        g.FillRectangle(fill, 6, 10, Math.Max(1, (int)Math.Round(17 * level.Value / 100.0)), 11);
                }
                else
                {
                    using (var dash = new Pen(Color.Silver, 2)) g.DrawLine(dash, 10, 15, 19, 15);
                }
                IntPtr native = bitmap.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(native)) return (Icon)borrowed.Clone();
                }
                finally { DestroyIcon(native); }
            }
        }
    }

    internal sealed class Tray : ApplicationContext
    {
        internal const int IntervalSeconds = 60;
        private readonly Control dispatcher = new Control();
        private readonly Timer timer = new Timer { Interval = IntervalSeconds * 1000 };
        private readonly BackgroundWorker worker = new BackgroundWorker();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray;
        private readonly ToolStripItem refresh;
        private readonly BatteryAlert alerts = new BatteryAlert();
        private DetailsForm details;
        private Snapshot snapshot;
        private Icon icon;
        private int? iconLevel;
        private bool closing;

        public Tray()
        {
            dispatcher.CreateControl();
            menu.Items.Add("查看电量", null, delegate { OpenDetails(); });
            refresh = menu.Items.Add("立即刷新", null, delegate { Refresh(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            icon = BatteryIcon.Draw(null);
            tray = new NotifyIcon { Icon = icon, Text = "余电 · 正在读取", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenDetails(); };
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = Monitor.Scan(); };
            worker.RunWorkerCompleted += OnResult;
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            // Start on the message loop, after the WinForms synchronization context is installed.
            dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            if (closing || worker.IsBusy) return;
            refresh.Enabled = false;
            if (details != null) details.SetBusy(true);
            worker.RunWorkerAsync();
        }

        private void OnResult(object sender, RunWorkerCompletedEventArgs e)
        {
            if (closing) return;
            snapshot = e.Error == null ? (Snapshot)e.Result : new Snapshot(new List<DeviceStatus>(), true);
            int? current = snapshot.Lowest;
            if (current != iconLevel)
            {
                Icon previous = icon;
                icon = BatteryIcon.Draw(current);
                tray.Icon = icon;
                iconLevel = current;
                previous.Dispose();
            }
            tray.Text = snapshot.Tooltip();
            refresh.Enabled = true;
            if (details != null) { details.Display(snapshot); details.SetBusy(false); }
            var low = snapshot.Devices.Where(d => alerts.ShouldNotify(d.Key, d.Battery)).ToList();
            if (low.Count != 0)
                tray.ShowBalloonTip(5000, "外设电量较低", string.Join("，", low.Select(d => d.Name + " " + d.Battery.Display)), ToolTipIcon.Warning);
        }

        private void OpenDetails()
        {
            if (details == null)
            {
                details = new DetailsForm(Refresh);
                details.FormClosed += delegate { details = null; };
            }
            if (snapshot != null) details.Display(snapshot);
            details.SetBusy(worker.IsBusy);
            if (details.WindowState == FormWindowState.Minimized) details.WindowState = FormWindowState.Normal;
            details.Show();
            details.Activate();
        }

        protected override void ExitThreadCore()
        {
            closing = true;
            timer.Stop();
            tray.Visible = false;
            if (details != null) details.Close();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                worker.RunWorkerCompleted -= OnResult;
                timer.Dispose();
                tray.Dispose();
                icon.Dispose();
                menu.Dispose();
                worker.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
