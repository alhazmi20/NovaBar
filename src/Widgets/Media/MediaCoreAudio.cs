// Core Audio (WASAPI endpoint) interop declared by hand + a tiny event-driven service.
// No polling: IAudioEndpointVolumeCallback pushes volume/mute changes, IMMNotificationClient pushes
// default-device / device-list changes. All COM calls run on the thread pool, serialized by a lock;
// callbacks only publish immutable snapshots (never take the lock) so nothing can deadlock.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NovaBar
{
    // ---------------- COM declarations ----------------
    [StructLayout(LayoutKind.Sequential)]
    public struct MediaPropKey { public Guid fmtid; public int pid; public MediaPropKey(Guid g, int p) { fmtid = g; pid = p; } }

    [StructLayout(LayoutKind.Explicit)]
    public struct MediaPropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
        [FieldOffset(16)] public IntPtr p2; // pads to the x64 PROPVARIANT size (24 bytes)
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out MediaIMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out MediaIMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out MediaIMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(MediaIMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(MediaIMMNotificationClient client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out MediaIMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out MediaIPropertyStore props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out MediaPropKey key);
        [PreserveSig] int GetValue(ref MediaPropKey key, out MediaPropVariant value);
        [PreserveSig] int SetValue(ref MediaPropKey key, ref MediaPropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(MediaIAudioEndpointVolumeCallback cb);
        [PreserveSig] int UnregisterControlChangeNotify(MediaIAudioEndpointVolumeCallback cb);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(int ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(int ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out int step, out int count);
        [PreserveSig] int VolumeStepUp(ref Guid ctx);
        [PreserveSig] int VolumeStepDown(ref Guid ctx);
        [PreserveSig] int QueryHardwareSupport(out int mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float inc);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, MediaPropKey key);
    }

    /// <summary>Undocumented but stable since Windows 7 (used by SoundSwitch, AudioDeviceCmdlets...). Only SetDefaultEndpoint is called.</summary>
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface MediaIPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr ep, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    // ---------------- snapshots ----------------
    /// <summary>Immutable state of one default endpoint (render or capture).</summary>
    public sealed class MediaEndpointSnap
    {
        public static readonly MediaEndpointSnap None = new MediaEndpointSnap();
        public bool Ok; public string Id = "", Name = ""; public float Volume; public bool Muted;
        public MediaEndpointSnap With(float vol, bool muted) { return new MediaEndpointSnap { Ok = Ok, Id = Id, Name = Name, Volume = vol, Muted = muted }; }
    }
    public sealed class MediaAudioDevice { public string Id, Name; }

    // ---------------- service ----------------
    public static class MediaAudio
    {
        const int eRender = 0, eCapture = 1, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23;
        static readonly MediaPropKey PKEY_FriendlyName = new MediaPropKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static Guid IID_EndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static Guid Ctx = Guid.Empty;

        static readonly object L = new object();             // serializes all COM work
        static readonly HashSet<Widget> subs = new HashSet<Widget>();
        static MediaIMMDeviceEnumerator en;
        static Notify notify;
        static Endpoint render = new Endpoint(eRender), capture = new Endpoint(eCapture);
        static volatile List<MediaAudioDevice> devices = new List<MediaAudioDevice>();
        static int gen;

        public static MediaEndpointSnap Output { get { return render.Snap; } }
        public static MediaEndpointSnap Mic { get { return capture.Snap; } }
        public static List<MediaAudioDevice> Devices { get { return devices; } }

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref MediaPropVariant pv);

        public static void Start(Widget w)
        {
            bool first;
            lock (subs) { first = subs.Add(w) && subs.Count == 1; }
            if (first) { int g = Interlocked.Increment(ref gen); Queue(() => Init(g)); }
        }
        public static void Stop(Widget w)
        {
            bool last;
            lock (subs) { last = subs.Remove(w) && subs.Count == 0; }
            if (last) { int g = Interlocked.Increment(ref gen); Queue(() => { if (g == gen) Teardown(); }); }
        }

        static void Queue(Action a) { ThreadPool.QueueUserWorkItem(_ => { lock (L) { try { a(); } catch { } } }); }
        internal static void Changed() { Widget[] ws; lock (subs) ws = subs.ToArray(); foreach (var w in ws) w.Invalidate(); }

        static void Init(int g)
        {
            if (g != gen || en != null) return;
            en = (MediaIMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
            notify = new Notify();
            en.RegisterEndpointNotificationCallback(notify);
            render.Bind(en); capture.Bind(en);
            LoadDevices();
            Changed();
        }
        static void Teardown()
        {
            render.Unbind(); capture.Unbind();
            if (en != null)
            {
                try { if (notify != null) en.UnregisterEndpointNotificationCallback(notify); } catch { }
                Release(en); en = null; notify = null;
            }
        }
        internal static void Release(object o) { try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { } }

        internal static string FriendlyName(MediaIMMDevice d)
        {
            MediaIPropertyStore ps = null;
            try
            {
                if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return "";
                var k = PKEY_FriendlyName; MediaPropVariant v;
                if (ps.GetValue(ref k, out v) != 0) return "";
                string s = v.vt == 31 && v.p != IntPtr.Zero ? Marshal.PtrToStringUni(v.p) : "";
                PropVariantClear(ref v);
                return s ?? "";
            }
            catch { return ""; }
            finally { Release(ps); }
        }

        static void LoadDevices()
        {
            var list = new List<MediaAudioDevice>();
            MediaIMMDeviceCollection col = null;
            try
            {
                if (en == null || en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out col) != 0 || col == null) return;
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    MediaIMMDevice d = null;
                    try
                    {
                        if (col.Item(i, out d) != 0 || d == null) continue;
                        string id; d.GetId(out id);
                        list.Add(new MediaAudioDevice { Id = id, Name = FriendlyName(d) });
                    }
                    finally { Release(d); }
                }
            }
            catch { }
            finally { Release(col); }
            devices = list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ---- actions (all async, optimistic snapshot update for instant feedback) ----
        public static void SetVolume(float v)
        {
            v = Math.Max(0f, Math.Min(1f, v));
            var s = render.Snap; if (!s.Ok) return;
            render.Snap = s.With(v, v > 0 ? false : s.Muted); Changed();
            Queue(() => { var ev = render.Vol; if (ev == null) return; ev.SetMasterVolumeLevelScalar(v, ref Ctx); if (v > 0 && s.Muted) ev.SetMute(false, ref Ctx); });
        }
        public static void Nudge(int delta) { var s = render.Snap; if (s.Ok) SetVolume((float)(Math.Round(s.Volume * 50) + delta) / 50f); }
        public static void ToggleOutputMute() { Toggle(render); }
        public static void ToggleMicMute() { Toggle(capture); }
        static void Toggle(Endpoint e)
        {
            var s = e.Snap; if (!s.Ok) return;
            bool m = !s.Muted; e.Snap = s.With(s.Volume, m); Changed();
            Queue(() => { var ev = e.Vol; if (ev != null) ev.SetMute(m, ref Ctx); });
        }
        public static void SetDefaultOutput(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Queue(() =>
            {
                object o = null;
                try
                {
                    o = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")));
                    var pc = (MediaIPolicyConfig)o;
                    for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role); // console, multimedia, communications
                }
                finally { Release(o); }
            });
        }
        public static void RefreshDevices() { Queue(() => { LoadDevices(); Changed(); }); }

        // ---- one default endpoint with a live volume callback ----
        sealed class Endpoint
        {
            readonly int flow;
            public volatile MediaEndpointSnap Snap = MediaEndpointSnap.None;
            public MediaIAudioEndpointVolume Vol;
            MediaIMMDevice dev; VolCallback cb;
            public Endpoint(int flow) { this.flow = flow; }

            public void Bind(MediaIMMDeviceEnumerator e)
            {
                Unbind();
                MediaIMMDevice d;
                if (e.GetDefaultAudioEndpoint(flow, 0, out d) != 0 || d == null) { Snap = MediaEndpointSnap.None; return; }
                dev = d;
                string id; d.GetId(out id);
                object o;
                if (d.Activate(ref IID_EndpointVolume, CLSCTX_ALL, IntPtr.Zero, out o) != 0 || o == null) { Snap = MediaEndpointSnap.None; return; }
                Vol = (MediaIAudioEndpointVolume)o;
                float v; bool m; Vol.GetMasterVolumeLevelScalar(out v); Vol.GetMute(out m);
                Snap = new MediaEndpointSnap { Ok = true, Id = id ?? "", Name = FriendlyName(d), Volume = v, Muted = m };
                cb = new VolCallback(this);
                Vol.RegisterControlChangeNotify(cb);
            }
            public void Unbind()
            {
                if (Vol != null) { try { if (cb != null) Vol.UnregisterControlChangeNotify(cb); } catch { } Release(Vol); }
                Release(dev);
                Vol = null; dev = null; cb = null;
            }
            public void OnNotify(bool muted, float vol)
            {
                var s = Snap; if (!s.Ok) return;
                if (Math.Abs(s.Volume - vol) < 0.0005f && s.Muted == muted) return;
                Snap = s.With(vol, muted); Changed();
            }
        }

        [ComVisible(true)]
        sealed class VolCallback : MediaIAudioEndpointVolumeCallback
        {
            readonly Endpoint ep;
            public VolCallback(Endpoint e) { ep = e; }
            public int OnNotify(IntPtr data)
            {
                // AUDIO_VOLUME_NOTIFICATION_DATA { GUID ctx; BOOL bMuted; float fMasterVolume; ... }
                try
                {
                    bool muted = Marshal.ReadInt32(data, 16) != 0;
                    float vol = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
                    ep.OnNotify(muted, vol);
                }
                catch { }
                return 0;
            }
        }

        [ComVisible(true)]
        sealed class Notify : MediaIMMNotificationClient
        {
            // Never call Core Audio synchronously from inside these callbacks: hop to the pool.
            public int OnDeviceStateChanged(string id, int state) { Queue(() => { LoadDevices(); Changed(); }); return 0; }
            public int OnDeviceAdded(string id) { return 0; }
            public int OnDeviceRemoved(string id) { return 0; }
            public int OnDefaultDeviceChanged(int flow, int role, string id)
            {
                if (role != 0) return 0; // eConsole only (each change fires once per role)
                Queue(() => { if (en == null) return; if (flow == eRender) render.Bind(en); else if (flow == eCapture) capture.Bind(en); Changed(); });
                return 0;
            }
            public int OnPropertyValueChanged(string id, MediaPropKey key)
            {
                if (key.fmtid == PKEY_FriendlyName.fmtid && key.pid == PKEY_FriendlyName.pid)
                    Queue(() => { LoadDevices(); if (en != null) { if (render.Snap.Id == id) render.Bind(en); if (capture.Snap.Id == id) capture.Bind(en); } Changed(); });
                return 0;
            }
        }
    }
}
