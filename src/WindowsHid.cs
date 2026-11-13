using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PeripheralCharge
{
    internal sealed class Endpoint
    {
        public string Path, Product;
        public int Vid, Pid, Page, Usage, InputSize, OutputSize, FeatureSize;
        public string Id { get { return Vid.ToString("X4") + ":" + Pid.ToString("X4"); } }
    }

    internal static class WindowsHid
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Attributes { public int Size; public ushort Vid, Pid, Version; }

        // HIDP_CAPS has 32 USHORT fields. Only its first five are used here.
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct Caps { public ushort Usage, Page, Input, Output, Feature; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Overlap
        {
            public IntPtr Internal, InternalHigh;
            public uint Offset, OffsetHigh;
            public IntPtr Event;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_Interface_List_SizeW(out uint chars, ref Guid guid, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_Interface_ListW(ref Guid guid, string id, IntPtr result, uint chars, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
        [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] private static extern byte HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] StringBuilder name, int bytes);
        [DllImport("hid.dll")][return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_FlushQueue(SafeFileHandle handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, int size, out int done, IntPtr overlap);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, int size, out int done, IntPtr overlap);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputSize, IntPtr output, int outputSize, out int done, IntPtr overlap);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlap, out int done, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlap);

        private static string[] InterfacePaths()
        {
            Guid guid;
            HidD_GetHidGuid(out guid);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint chars;
                int status = CM_Get_Device_Interface_List_SizeW(out chars, ref guid, null, 0);
                if (status != 0) throw new InvalidOperationException("HID list size: CR " + status);
                if (chars <= 1) return new string[0];
                IntPtr memory = Marshal.AllocHGlobal(checked((int)chars * 2));
                try
                {
                    status = CM_Get_Device_Interface_ListW(ref guid, null, memory, chars, 0);
                    if (status == 0x1a) continue; // Hot-plug between sizing and reading.
                    if (status != 0) throw new InvalidOperationException("HID list: CR " + status);
                    return Marshal.PtrToStringUni(memory, (int)chars).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            throw new InvalidOperationException("HID list changed during enumeration.");
        }

        public static List<Endpoint> Enumerate(bool includeUnknown)
        {
            var devices = new List<Endpoint>();
            foreach (string path in InterfacePaths())
            {
                string lower = path.ToLowerInvariant();
                if (!lower.Contains("vid_")) continue;
                if (!includeUnknown && !lower.Contains("vid_5253&pid_1021") && !lower.Contains("vid_41e4&pid_2001")
                    && !lower.Contains("vid_3554&pid_fa09") && !lower.Contains("vid_05ac&pid_024f")) continue;
                using (SafeFileHandle handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    var attr = new Attributes();
                    attr.Size = Marshal.SizeOf(typeof(Attributes));
                    IntPtr prep;
                    if (handle.IsInvalid || !HidD_GetAttributes(handle, ref attr) || !HidD_GetPreparsedData(handle, out prep)) continue;
                    Caps caps;
                    try { if (HidP_GetCaps(prep, out caps) != 0x110000) continue; }
                    finally { HidD_FreePreparsedData(prep); }
                    if (!includeUnknown && caps.Page < 0xff00) continue;
                    var name = new StringBuilder(128);
                    bool named = HidD_GetProductString(handle, name, name.Capacity * 2);
                    devices.Add(new Endpoint { Path = path, Product = named ? name.ToString().Trim() : "",
                        Vid = attr.Vid, Pid = attr.Pid, Page = caps.Page, Usage = caps.Usage,
                        InputSize = caps.Input, OutputSize = caps.Output, FeatureSize = caps.Feature });
                }
            }
            devices.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Path, y.Path));
            return devices;
        }

        private enum Transfer { Input, Output, GetFeature, SetFeature }

        // Retain the event, OVERLAPPED and pinned buffer until cancellation has actually completed.
        private static int Report(SafeFileHandle handle, Transfer direction, byte[] bytes, int timeout)
        {
            using (var signal = new EventWaitHandle(false, EventResetMode.ManualReset))
            {
                IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlap)));
                GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    Marshal.StructureToPtr(new Overlap { Event = signal.SafeWaitHandle.DangerousGetHandle() }, ov, false);
                    IntPtr data = pin.AddrOfPinnedObject();
                    int size = bytes.Length, done;
                    bool ok;
                    switch (direction)
                    {
                        case Transfer.Input: ok = ReadFile(handle, data, size, out done, ov); break;
                        case Transfer.Output: ok = WriteFile(handle, data, size, out done, ov); break;
                        case Transfer.SetFeature:
                            ok = DeviceIoControl(handle, 0x000b0191, data, size, IntPtr.Zero, 0, out done, ov); break;
                        default: // IOCTL_HID_GET_FEATURE uses the report ID in the request buffer.
                            ok = DeviceIoControl(handle, 0x000b0192, data, size, data, size, out done, ov); break;
                    }
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 997) throw new Win32Exception(error);
                        if (!signal.WaitOne(timeout))
                        {
                            CancelIoEx(handle, ov);
                            GetOverlappedResult(handle, ov, out done, true);
                            throw new TimeoutException();
                        }
                        if (!GetOverlappedResult(handle, ov, out done, false)) throw new Win32Exception();
                    }
                    return done;
                }
                finally { pin.Free(); Marshal.FreeHGlobal(ov); }
            }
        }

        public static Reading ReadBattery(Endpoint e)
        {
            Model model = Protocol.Identify(e);
            if (model == Model.Other) return new Reading(State.Unsupported);
            using (SafeFileHandle handle = CreateFileW(e.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) return Failure(Marshal.GetLastWin32Error());
                try
                {
                    if (model == Model.A7)
                    {
                        Report(handle, Transfer.SetFeature, Protocol.Query(model, e.FeatureSize), 700);
                        Thread.Sleep(120);
                        byte[] answer = new byte[e.FeatureSize];
                        answer[0] = 0x11;
                        int count = Report(handle, Transfer.GetFeature, answer, 1000);
                        return Protocol.MouseReply(answer, count);
                    }
                    HidD_FlushQueue(handle);
                    byte[] query = Protocol.Query(model, e.OutputSize);
                    if (Report(handle, Transfer.Output, query, 700) != query.Length) return new Reading(State.Error);
                    var deadline = Stopwatch.StartNew();
                    byte[] input = new byte[e.InputSize];
                    while (deadline.ElapsedMilliseconds < 1500)
                    {
                        int remaining = Math.Max(1, 1500 - (int)deadline.ElapsedMilliseconds);
                        int count = Report(handle, Transfer.Input, input, remaining);
                        if (count == 0) return new Reading(State.NoReply);
                        Reading answer = model == Model.Ganss ? Protocol.GanssReply(input, count) : Protocol.KeyboardReply(input, count);
                        if (answer != null) return answer;
                    }
                    return new Reading(State.NoReply);
                }
                catch (TimeoutException) { return new Reading(State.NoReply); }
                catch (Win32Exception ex) { return Failure(ex.NativeErrorCode); }
            }
        }

        private static Reading Failure(int error)
        {
            if (error == 5 || error == 32) return new Reading(State.Busy);
            if (error == 1167 || error == 2 || error == 31) return new Reading(State.NoReply);
            return new Reading(State.Error);
        }
    }
}
