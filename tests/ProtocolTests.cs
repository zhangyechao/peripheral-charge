using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralCharge
{
    internal static class ProtocolTests
    {
        private static int passed;
        private static void Check(string name, bool value)
        {
            if (!value) throw new InvalidOperationException(name);
            passed++;
        }

        private static void Mouse()
        {
            byte[] packet = Protocol.Query(Model.A7, 65);
            Check("65-byte A7 feature request", packet.Length == 65 && packet[0] == 0x11 &&
                packet[1] == 0xf9 && packet.Skip(2).All(b => b == 0xff));
            Check("an echoed request has no identity", Protocol.MouseReply(packet, 65).State == State.Invalid);
            // Raw prefix in marsuyone's protocol note. The conventional status byte is 0x9e XOR 0xff.
            byte[] prefix = { 0x11, 0xf9, 0xac, 0xad, 0x8f, 0xff, 0xf9, 0xfa, 0xfc, 0xfd, 0xf6, 0x9e, 0xff, 0x62, 0xff };
            byte[] report = new byte[65];
            Array.Copy(prefix, report, prefix.Length);
            Check("complete Windows status offset 11", Protocol.MouseReply(report, 65).Percent == 97);
            report[11] = 0xff;
            Check("valid zero", Protocol.MouseReply(report, 65).Percent == 0);
            report[11] = 0x9b;
            report[12] = 0xfe;
            Reading full = Protocol.MouseReply(report, 65);
            Check("100 percent while charging", full.Percent == 100 && full.Charging);
            report[10] = 0xfe;
            Check("wireless link is down", Protocol.MouseReply(report, 65).State == State.Unlinked);
            report[10] = 0xf6;
            report[11] = 0x9a;
            Check("reject 101", Protocol.MouseReply(report, 65).State == State.Invalid);
            Check("short report", Protocol.MouseReply(report, 6).Percent == null);
            report[1] = 0;
            Check("wrong mouse command", Protocol.MouseReply(report, 65).State == State.Invalid);
        }

        private static void Keyboards()
        {
            byte[] query = Protocol.Query(Model.G87, 20);
            Check("G87 command", query.Length == 20 && query[0] == 0x13 && query[1] == 0x4a &&
                query[2] == 1 && query.Skip(3).All(b => b == 0));
            Check("ignore echoed status query", Protocol.KeyboardReply(query, 20) == null);
            byte[] sample = { 0x13, 0x4a, 1, 0, 2, 0x34, 1 };
            Check("G87 52 percent sample", Protocol.KeyboardReply(sample, 7).Percent == 52);
            sample[5] = 0;
            Check("keyboard zero", Protocol.KeyboardReply(sample, 7).Percent == 0);
            sample[5] = 100;
            Check("keyboard full", Protocol.KeyboardReply(sample, 7).Percent == 100);
            sample[5] = 255;
            Check("keyboard sentinel", Protocol.KeyboardReply(sample, 7).State == State.Invalid);
            Check("truncated advertised length", Protocol.KeyboardReply(sample, 6).State == State.Invalid);
            sample[1] = 0x0a;
            Check("unsolicited events skipped", Protocol.KeyboardReply(sample, 7) == null);
            byte[] capacity = { 0x06, 0, 2, 100, 100, 100, 0xb5, 0x70 };
            Check("capacity is never current battery", Protocol.KeyboardReply(capacity, 8) == null);
            byte[] compx = Protocol.Query(Model.Compx, 20);
            Check("Compx checksum in byte 19", compx[19] == compx.Take(19).Sum(b => (int)b) % 256);
        }

        private static void Identity()
        {
            var device = new Endpoint { Vid = 0x5253, Pid = 0x1021, Page = 0xff01, FeatureSize = 65 };
            Check("A7 exact match", Protocol.Identify(device) == Model.A7);
            device.Pid++;
            Check("another mouse under same VID", Protocol.Identify(device) == Model.Other);
            device = new Endpoint { Vid = 0x3554, Pid = 0xfa09, Page = 0xff02, Usage = 2, InputSize = 20, OutputSize = 20 };
            Check("Compx status endpoint", Protocol.Identify(device) == Model.Compx);
            device.Page = 0xff04;
            Check("exclude capacity collection", Protocol.Identify(device) == Model.Other);
            device = new Endpoint { Vid = 0x41e4, Pid = 0x2001, Page = 0xff02, Usage = 2, InputSize = 20, OutputSize = 20 };
            Check("G87 exact endpoint", Protocol.Identify(device) == Model.G87);
            device.Page = 1;
            Check("never read typing reports", Protocol.Identify(device) == Model.Other);
        }

        private static void Ganss()
        {
            var e = new Endpoint { Vid = 0x05ac, Pid = 0x024f, Product = "GS3104T-PRO 2.4G",
                Page = 0xff60, Usage = 0x61, InputSize = 33, OutputSize = 33 };
            Check("Ganss named 32-byte collection", Protocol.Identify(e) == Model.Ganss);
            e.InputSize = e.OutputSize = 65;
            Check("skip second vendor collection", Protocol.Identify(e) == Model.Other);
            e.InputSize = e.OutputSize = 33; e.Product = "Other keyboard";
            Check("shared Apple VID/PID does not authorize a command", Protocol.Identify(e) == Model.Other);
            byte[] reply = Protocol.Query(Model.Ganss, 33);
            Check("Ganss query layout", reply[0] == 0 && reply[1] == 0x20 && reply[2] == 1 &&
                reply.Skip(3).Take(29).All(b => b == 0) && reply[32] == 0x21);
            Check("echo cannot masquerade as empty battery", Protocol.GanssReply(reply, 33).Percent == null);
            reply[4] = 43; reply[32] = 0x4c;
            Check("Ganss hardware reply", Protocol.GanssReply(reply, 33).Percent == 43);
            Check("incomplete Ganss reply", Protocol.GanssReply(reply, 5).State == State.Invalid);
            reply[32] ^= 1;
            Check("reject corrupt checksum", Protocol.GanssReply(reply, 33).State == State.Invalid);
            reply[4] = 100; reply[32] = 0x85;
            Check("Ganss full", Protocol.GanssReply(reply, 33).Percent == 100);
            reply[4] = 101; reply[32]++;
            Check("Ganss invalid percent", Protocol.GanssReply(reply, 33).Percent == null);
            reply[1] = 0x0f;
            Check("ignore unrelated vendor event", Protocol.GanssReply(reply, 33) == null);
        }

        private static void Presentation()
        {
            var alarm = new BatteryAlert();
            Check("first low", alarm.ShouldNotify("A", new Reading(15, false)));
            Check("no repeat", !alarm.ShouldNotify("A", new Reading(8, false)));
            Check("timeout does not reset", !alarm.ShouldNotify("A", new Reading(State.NoReply)) &&
                !alarm.ShouldNotify("A", new Reading(7, false)));
            Check("independent receivers", alarm.ShouldNotify("B", new Reading(9, false)));
            Check("small fluctuation", !alarm.ShouldNotify("A", new Reading(17, false)) &&
                !alarm.ShouldNotify("A", new Reading(15, false)));
            Check("hysteresis", !alarm.ShouldNotify("A", new Reading(20, false)) && alarm.ShouldNotify("A", new Reading(0, false)));
            Check("charging suppresses reminder", !alarm.ShouldNotify("A", new Reading(9, true)));
            var devices = new List<DeviceStatus>();
            var none = new Snapshot(devices, false);
            Check("no fake zero when empty", none.Lowest == null && !none.Tooltip().Contains("0%"));
            devices.Add(new DeviceStatus("a", "测试键盘", "", new Reading(State.NoReply)));
            Check("missing is shown", none.Lowest == null && none.Tooltip().Contains("无应答"));
            for (int i = 0; i < 12; i++) devices.Add(new DeviceStatus(i.ToString(), "测试设备 " + i, "", new Reading(20 + i, false)));
            Check("tooltip length and overflow", none.Tooltip().Length <= 63 && none.Tooltip().Contains("另有"));
            Check("minimum ignores missing", none.Lowest == 20);
        }

        public static int Main()
        {
            try
            {
                Mouse(); Keyboards(); Identity(); Ganss(); Presentation();
                Console.WriteLine(passed + " checks passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAILED: " + ex.Message); return 1; }
        }
    }
}
