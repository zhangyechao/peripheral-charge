using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeripheralCharge
{
    // WinForms NotifyIcon assigns IDs in creation order. A Shell GUID lets Windows
    // remember which physical receiver the user put next to the clock.
    internal sealed class TaskbarIcons : NativeWindow, IDisposable
    {
        private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
        private const uint WithMessage = 1, WithIcon = 2, WithTip = 4, WithInfo = 0x10, WithGuid = 0x20, ShowTip = 0x80;
        private const int Callback = 0x8001, ContextMenuMessage = 0x7b, Select = 0x400, KeySelect = 0x401, BalloonClick = 0x405;
        private readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private readonly Action open;
        private readonly ContextMenuStrip menu;
        private readonly Dictionary<Guid, Item> items = new Dictionary<Guid, Item>();
        private uint nextId;

        private sealed class Item
        {
            public uint LocalId;
            public DeviceBadge Badge;
            public Icon Icon;
            public int Size;
            public bool HighContrast, Registered;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct IconData
        {
            public int Size;
            public IntPtr Window;
            public uint Id, Flags, Message;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconIdentifier { public int Size; public IntPtr Window; public uint Id; public Guid Guid; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint command, ref IconData data);
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier item, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string type, string title);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

        public TaskbarIcons(Action open, ContextMenuStrip menu)
        {
            this.open = open;
            this.menu = menu;
            CreateHandle(new CreateParams { Caption = "PeripheralCharge.Tray", Style = unchecked((int)0x80000000), ExStyle = 0x80 });
        }

        private static int IconSize()
        {
            try
            {
                uint dpi = GetDpiForWindow(FindWindow("Shell_TrayWnd", null));
                if (dpi > 0)
                {
                    int width = GetSystemMetricsForDpi(49, dpi); // SM_CXSMICON
                    if (width > 0) return width;
                }
            }
            catch (EntryPointNotFoundException) { /* Earlier Windows 10 releases. */ }
            return SystemInformation.SmallIconSize.Width;
        }

        public void Update(IEnumerable<DeviceBadge> badges)
        {
            var keep = new HashSet<Guid>();
            int size = IconSize();
            bool contrast = SystemInformation.HighContrast;
            foreach (var badge in badges)
            {
                keep.Add(badge.Id);
                Item item;
                if (!items.TryGetValue(badge.Id, out item))
                {
                    item = new Item { LocalId = ++nextId };
                    items.Add(badge.Id, item);
                }
                bool redraw = item.Icon == null || item.Badge.Percent != badge.Percent || item.Size != size || item.HighContrast != contrast;
                bool changed = redraw || item.Badge.Tooltip != badge.Tooltip;
                if (redraw)
                {
                    Icon previous = item.Icon;
                    item.Icon = BadgeIcon.Draw(badge.Percent, size, contrast);
                    if (previous != null) previous.Dispose();
                }
                item.Badge = badge;
                item.Size = size;
                item.HighContrast = contrast;
                if (changed || !item.Registered) Publish(item);
            }
            foreach (Guid id in items.Keys.Where(id => !keep.Contains(id)).ToArray()) Remove(id);
        }

        private IconData Data(Item item, uint flags)
        {
            return new IconData { Size = Marshal.SizeOf(typeof(IconData)), Window = Handle, Id = item.LocalId,
                Flags = WithGuid | flags, Guid = item.Badge.Id };
        }

        private static string Fit(string value, int max)
        {
            if (value.Length <= max) return value;
            int end = char.IsHighSurrogate(value[max - 2]) ? max - 2 : max - 1;
            return value.Substring(0, end) + "…";
        }

        private void Publish(Item item)
        {
            var data = Data(item, WithMessage | WithIcon | WithTip | ShowTip);
            data.Message = Callback;
            data.Icon = item.Icon.Handle;
            data.Tip = Fit(item.Badge.Tooltip, 127);
            if (item.Registered && Shell_NotifyIconW(Modify, ref data)) return;
            item.Registered = Shell_NotifyIconW(Add, ref data) || Shell_NotifyIconW(Modify, ref data);
            if (item.Registered)
            {
                data.Version = 4;
                Shell_NotifyIconW(SetVersion, ref data);
            }
        }

        public void Notify(Guid owner, string message)
        {
            Item item;
            if (!items.TryGetValue(owner, out item) || !item.Registered) return;
            var data = Data(item, WithInfo);
            data.InfoFlags = 2; // NIIF_WARNING
            data.Title = "外设电量较低";
            data.Info = Fit(message, 255);
            Shell_NotifyIconW(Modify, ref data);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbarCreated && taskbarCreated != 0)
            {
                foreach (var item in items.Values) { item.Registered = false; Publish(item); }
            }
            else if (m.Msg == Callback)
            {
                int packed = unchecked((int)m.LParam.ToInt64());
                switch (packed & 0xffff)
                {
                    case 0x202: case Select: case KeySelect: case BalloonClick: open(); break; // WM_LBUTTONUP or Shell activation
                    case ContextMenuMessage:
                        uint id = (uint)packed >> 16;
                        Item item = items.Values.FirstOrDefault(i => i.LocalId == id);
                        Point anchor = Cursor.Position;
                        if (item != null)
                        {
                            var key = new IconIdentifier { Size = Marshal.SizeOf(typeof(IconIdentifier)),
                                Window = Handle, Id = id, Guid = item.Badge.Id };
                            Rect rect;
                            if (Shell_NotifyIconGetRect(ref key, out rect) == 0)
                                anchor = new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
                        }
                        SetForegroundWindow(Handle);
                        menu.Show(anchor);
                        break;
                }
            }
            else base.WndProc(ref m);
        }

        private void Remove(Guid id)
        {
            var item = items[id];
            var data = Data(item, 0);
            Shell_NotifyIconW(Delete, ref data);
            item.Icon.Dispose();
            items.Remove(id);
        }

        public void Dispose()
        {
            foreach (Guid id in items.Keys.ToArray()) Remove(id);
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
