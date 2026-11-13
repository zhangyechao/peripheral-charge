using System;
using System.Collections.Generic;

namespace PeripheralCharge
{
    internal enum Model { Other, A7, G87, Compx, Ganss }
    internal enum State { Ready, NoReply, Unlinked, Busy, Invalid, Unsupported, Error }

    internal sealed class Reading
    {
        public readonly State State;
        public readonly int? Percent;
        public readonly bool Charging;

        public Reading(State state) { State = state; }
        public Reading(int percent, bool charging)
        {
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException("percent");
            State = State.Ready;
            Percent = percent;
            Charging = charging;
        }

        public string Display
        {
            get
            {
                switch (State)
                {
                    case State.Ready: return Percent + "%" + (Charging ? " · 充电中" : "");
                    case State.NoReply: return "无应答";
                    case State.Unlinked: return "未连接";
                    case State.Busy: return "无法访问";
                    case State.Invalid: return "读数无效";
                    case State.Unsupported: return "待适配";
                    default: return "读取失败";
                }
            }
        }
    }

    internal static class Protocol
    {
        public static Model Identify(Endpoint e)
        {
            if (e.Vid == 0x5253 && e.Pid == 0x1021 && e.Page == 0xff01 && e.FeatureSize == 65)
                return Model.A7;
            if (e.Page == 0xff02 && e.InputSize >= 20 && e.OutputSize >= 20)
            {
                if (e.Vid == 0x41e4 && e.Pid == 0x2001) return Model.G87;
                if (e.Vid == 0x3554 && e.Pid == 0xfa09 && e.Usage == 2) return Model.Compx;
            }
            // This USB ID is shared with unrelated keyboards. Only identify the named product.
            if (e.Vid == 0x05ac && e.Pid == 0x024f && e.Page == 0xff60 && e.Usage == 0x61 &&
                e.InputSize == 33 && e.OutputSize == 33 &&
                string.Equals(e.Product, "GS3104T-PRO 2.4G", StringComparison.OrdinalIgnoreCase))
                return Model.Ganss;
            return Model.Other;
        }

        public static string Name(Model model)
        {
            switch (model)
            {
                case Model.A7: return "迈从 A7";
                case Model.G87: return "迈从 G87";
                case Model.Compx: return "Compx 无线键盘";
                case Model.Ganss: return "高斯 GS3104T PRO";
                default: return "其他设备";
            }
        }

        public static byte[] Query(Model model, int size)
        {
            if (model == Model.A7 && size == 65)
            {
                byte[] query = new byte[size];
                for (int i = 2; i < size; i++) query[i] = 0xff;
                query[0] = 0x11;
                query[1] = 0xf9;
                return query;
            }
            if ((model == Model.G87 || model == Model.Compx) && size >= 20)
            {
                byte[] query = new byte[size];
                query[0] = 0x13;
                query[1] = 0x4a;
                query[2] = 1;
                // The AULA/Compx variant sums the first 19 bytes; G87 sends zero here.
                if (model == Model.Compx) query[19] = 0x5e;
                return query;
            }
            if (model == Model.Ganss && size == 33)
            {
                byte[] query = new byte[size];
                query[1] = 0x20;
                query[2] = 0x01;
                query[32] = 0x21;
                return query;
            }
            throw new ArgumentException("No battery query for this collection.");
        }

        public static Reading MouseReply(byte[] frame, int length)
        {
            if (length < 13 || length > frame.Length || frame[0] != 0x11 || frame[1] != 0xf9)
                return new Reading(State.Invalid);
            int vendor = (frame[2] | frame[3] << 8) ^ 0xffff;
            int product = (frame[4] | frame[5] << 8) ^ 0xffff;
            if ((vendor != 0x5253 && vendor != 0x3837) || product == 0 || product == 0xffff)
                return new Reading(State.Invalid);
            int flags = frame[10] ^ 0xff;
            if ((flags & 7) > 2) return new Reading(State.Invalid);
            if ((flags & 7) != 0 && (flags & 8) == 0) return new Reading(State.Unlinked);
            int level = frame[11] ^ 0xff;
            if (level > 100) return new Reading(State.Invalid);
            return new Reading(level, (frame[12] ^ 0xff) == 1);
        }

        // Null identifies an unrelated input event or an echo, so the caller keeps waiting.
        public static Reading KeyboardReply(byte[] frame, int length)
        {
            if (length < 2 || length > frame.Length || frame[0] != 0x13 || frame[1] != 0x4a)
                return null;
            if (length < 5) return new Reading(State.Invalid);
            int payload = frame[4] & 15;
            if (payload == 0) return null;
            if (frame[2] != 1 || frame[3] != 0 || payload > 14 ||
                length < 5 + payload || frame[5] > 100) return new Reading(State.Invalid);
            return new Reading(frame[5], false);
        }

        public static Reading GanssReply(byte[] frame, int length)
        {
            if (length < 3 || length > frame.Length || frame[0] != 0 || frame[1] != 0x20 || frame[2] != 1)
                return null;
            if (length != 33 || frame[3] != 0 || frame[4] > 100) return new Reading(State.Invalid);
            int sum = 0;
            for (int i = 1; i < 32; i++) sum += frame[i];
            if ((sum & 0xff) != frame[32]) return new Reading(State.Invalid);
            // An all-zero battery payload is indistinguishable from a query echo.
            if (frame[4] == 0)
                return new Reading(State.Invalid);
            return new Reading(frame[4], false);
        }
    }

    internal sealed class BatteryAlert
    {
        private readonly HashSet<string> sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool ShouldNotify(string key, Reading reading)
        {
            if (!reading.Percent.HasValue) return false;
            if (reading.Charging || reading.Percent >= 20) sent.Remove(key);
            return !reading.Charging && reading.Percent <= 15 && sent.Add(key);
        }
    }
}
