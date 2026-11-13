using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralCharge
{
    internal sealed class DeviceStatus
    {
        public readonly string Key, Name, Id;
        public readonly Reading Battery;

        public DeviceStatus(string key, string name, string id, Reading battery)
        {
            Key = key; Name = name; Id = id; Battery = battery;
        }
    }

    internal sealed class Snapshot
    {
        public readonly List<DeviceStatus> Devices;
        public readonly DateTime Checked;
        public readonly bool ScanFailed;

        public Snapshot(List<DeviceStatus> devices, bool scanFailed)
        {
            Devices = devices; Checked = DateTime.Now; ScanFailed = scanFailed;
        }

        public int? Lowest
        {
            get { return Devices.Select(d => d.Battery.Percent).Min(); }
        }

        public string EmptyMessage
        {
            get { return ScanFailed ? "扫描失败，请重新刷新" : "未发现适配的接收器"; }
        }

        public string Tooltip()
        {
            if (Devices.Count == 0) return "余电 · " + EmptyMessage;
            var lines = new List<string>();
            foreach (DeviceStatus d in Devices) lines.Add(d.Name + "  " + d.Battery.Display);
            // .NET Framework NotifyIcon.Text accepts at most 63 UTF-16 code units.
            for (int count = lines.Count; count > 0; count--)
            {
                string tip = string.Join("\n", lines.Take(count));
                if (count < lines.Count) tip += "\n另有 " + (lines.Count - count) + " 台";
                if (tip.Length <= 63) return tip;
            }
            return "余电 · " + lines.Count + " 台设备";
        }
    }

    internal static class Monitor
    {
        public static Snapshot Scan()
        {
            var devices = new List<DeviceStatus>();
            foreach (var group in WindowsHid.Enumerate(false)
                .GroupBy(Protocol.Identify).Where(g => g.Key != Model.Other).OrderBy(g => g.Key))
            {
                int number = 0;
                foreach (Endpoint e in group)
                {
                    number++;
                    string name = Protocol.Name(group.Key);
                    if (group.Count() > 1) name += " #" + number;
                    devices.Add(new DeviceStatus(e.Path, name, e.Id, WindowsHid.ReadBattery(e)));
                }
            }
            return new Snapshot(devices, false);
        }
    }
}
