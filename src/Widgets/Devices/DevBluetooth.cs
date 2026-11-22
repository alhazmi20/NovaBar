// Bluetooth battery levels — port of wirless-charge/src/bluetooth.rs.
// 1) PnP property DEVPKEY_Bluetooth_Battery {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2 (what the Settings app shows),
//    read through Windows.Devices.Enumeration.Pnp.PnpObject exactly like the Rust code; instance ids embed the address.
// 2) GATT Battery Service 0x180F / Battery Level 0x2A19 fallback for BLE devices without (1).
// Runs on the Hub thread-pool thread (MTA), never on the UI thread.
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Enumeration.Pnp;
using Windows.Storage.Streams;

namespace NovaBar
{
    public static class DevBluetooth
    {
        const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
        const int OpMs = 6000;
        static int filterOk = 1; // AQS "property exists" filter; falls back to the unfiltered Rust query if Windows rejects it

        public static List<DevDevice> Scan()
        {
            var pnp = PnpBatteryLevels();
            Func<string, int> lookup = addr => { int best = -1; foreach (var kv in pnp) if (kv.Key.Contains(addr) && kv.Value > best) best = kv.Value; return best; };

            var devices = new List<DevDevice>();
            var seen = new HashSet<string>();

            // Classic (BR/EDR) devices: headphones, speakers, ...
            var sel = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            foreach (var info in DevWinRT.Wait(DeviceInformation.FindAllAsync(sel), OpMs))
            {
                BluetoothDevice dev = null;
                try { dev = DevWinRT.Wait(BluetoothDevice.FromIdAsync(info.Id), OpMs); } catch { }
                if (dev == null) continue;
                string address, name; DevKind kind = DevKind.Other;
                try
                {
                    address = dev.BluetoothAddress.ToString("X12");
                    name = dev.Name ?? "";
                    try { var cod = dev.ClassOfDevice; if (cod != null) kind = KindFromClassOfDevice(cod.RawValue); } catch { }
                }
                finally { try { dev.Dispose(); } catch { } }
                int battery = lookup(address);
                if (battery >= 0) { seen.Add(address); devices.Add(Make(address, name, kind, battery)); }
            }

            // Low Energy devices: mice, keyboards, earbuds, ...
            sel = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            foreach (var info in DevWinRT.Wait(DeviceInformation.FindAllAsync(sel), OpMs))
            {
                BluetoothLEDevice dev = null;
                try { dev = DevWinRT.Wait(BluetoothLEDevice.FromIdAsync(info.Id), OpMs); } catch { }
                if (dev == null) continue;
                try
                {
                    var address = dev.BluetoothAddress.ToString("X12");
                    if (seen.Contains(address)) continue;
                    var name = dev.Name ?? ""; DevKind kind = DevKind.Other;
                    try { var ap = dev.Appearance; if (ap != null) kind = KindFromAppearance(ap.RawValue); } catch { }
                    int battery = lookup(address);
                    if (battery < 0) battery = GattBatteryLevel(dev);
                    if (battery >= 0) { seen.Add(address); devices.Add(Make(address, name, kind, battery)); }
                }
                finally { try { dev.Dispose(); } catch { } }
            }
            return devices;
        }

        static DevDevice Make(string address, string name, DevKind kind, int battery)
        {
            if (kind == DevKind.Other) kind = DevKinds.FromName(name);
            return new DevDevice { Key = "bt:" + address, Name = name, Kind = kind, Battery = battery, Charging = false, Conn = DevConn.Bluetooth };
        }

        /// <summary>Classic Bluetooth Class of Device: major class in bits 8-12, minor in 2-7.</summary>
        public static DevKind KindFromClassOfDevice(uint cod)
        {
            uint major = (cod >> 8) & 0x1F, minor = (cod >> 2) & 0x3F;
            if (major == 4) return DevKind.Headset;                    // audio/video
            if (major == 5)
            {
                if ((minor & 0x20) != 0) return DevKind.Mouse;         // pointing (checked first: combo devices are usually mice)
                if ((minor & 0x10) != 0) return DevKind.Keyboard;
                uint m = minor & 0x0F; if (m == 1 || m == 2) return DevKind.Gamepad; // joystick / gamepad
            }
            return DevKind.Other;
        }

        /// <summary>BLE GAP Appearance: category in the upper 10 bits, subcategory in the lower 6.</summary>
        public static DevKind KindFromAppearance(ushort appearance)
        {
            int cat = appearance >> 6, sub = appearance & 0x3F;
            if (cat == 15 && sub == 1) return DevKind.Keyboard;
            if (cat == 15 && (sub == 2 || sub == 7)) return DevKind.Mouse;   // mouse, digitizer/pen
            if (cat == 15 && (sub == 3 || sub == 4)) return DevKind.Gamepad;
            if (cat >= 33 && cat <= 37) return DevKind.Headset;              // audio sink/source, wearable audio
            return DevKind.Other;
        }

        /// <summary>(uppercased PnP instance id, battery %) for every device node that has the Bluetooth battery property.</summary>
        static List<KeyValuePair<string, int>> PnpBatteryLevels()
        {
            var props = new List<string> { BatteryKey };
            PnpObjectCollection objs = null;
            if (filterOk == 1)
            {
                try { objs = DevWinRT.Wait(PnpObject.FindAllAsync(PnpObjectType.Device, props, BatteryKey + ":<>[]"), OpMs); }
                catch { filterOk = 0; objs = null; }
            }
            if (objs == null) objs = DevWinRT.Wait(PnpObject.FindAllAsync(PnpObjectType.Device, props), OpMs * 2);
            var outl = new List<KeyValuePair<string, int>>();
            foreach (var o in objs)
            {
                try
                {
                    object v;
                    if (o.Properties == null || !o.Properties.TryGetValue(BatteryKey, out v) || v == null) continue;
                    int level = Convert.ToInt32(v);
                    outl.Add(new KeyValuePair<string, int>((o.Id ?? "").ToUpperInvariant(), Math.Min(100, Math.Max(0, level))));
                }
                catch { }
            }
            return outl;
        }

        static int GattBatteryLevel(BluetoothLEDevice dev)
        {
            GattDeviceServicesResult services;
            try { services = DevWinRT.Wait(dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached), OpMs); }
            catch { return -1; }
            if (services == null || services.Status != GattCommunicationStatus.Success) return -1;
            int result = -1;
            foreach (var service in services.Services)
            {
                if (result < 0)
                {
                    try
                    {
                        var chars = DevWinRT.Wait(service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached), OpMs);
                        if (chars != null && chars.Status == GattCommunicationStatus.Success && chars.Characteristics.Count > 0)
                        {
                            var read = DevWinRT.Wait(chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached), OpMs);
                            if (read != null && read.Status == GattCommunicationStatus.Success && read.Value != null && read.Value.Length > 0)
                            {
                                var reader = DataReader.FromBuffer(read.Value);
                                result = Math.Min((int)reader.ReadByte(), 100);
                            }
                        }
                    }
                    catch { }
                }
                try { service.Dispose(); } catch { }
            }
            return result;
        }
    }
}
