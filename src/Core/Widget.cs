// Widget contract. Every widget is one class deriving from Widget with a [Widget(...)] attribute.
// Registry discovers widgets via reflection — no central list to edit.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Data source of a widget, used by the parity checklist.</summary>
    public enum DataKind { Live, NeedsAccount, Sample }

    [AttributeUsage(AttributeTargets.Class)]
    public class WidgetAttribute : Attribute
    {
        public string Id, Name, Category, Description;
        public string RefName;       // exact name of the matching widget in data/reference-widgets.json (default = Name)
        public string Account;       // Accounts id this widget needs (optional)
        public DataKind Data = DataKind.Live;
        public WidgetAttribute(string id, string name, string category, string description) { Id = id; Name = name; Category = category; Description = description; }
    }

    public enum SettingType { Toggle, Text, Number, Choice }
    public class SettingDef
    {
        public string Key, Label; public SettingType Type; public object Default; public string[] Options; public double Min, Max = 1e9;
        public static SettingDef Toggle(string k, string label, bool def) { return new SettingDef { Key = k, Label = label, Type = SettingType.Toggle, Default = def }; }
        public static SettingDef Text(string k, string label, string def) { return new SettingDef { Key = k, Label = label, Type = SettingType.Text, Default = def }; }
        public static SettingDef Number(string k, string label, double def, double min, double max) { return new SettingDef { Key = k, Label = label, Type = SettingType.Number, Default = def, Min = min, Max = max }; }
        public static SettingDef Choice(string k, string label, string def, params string[] opts) { return new SettingDef { Key = k, Label = label, Type = SettingType.Choice, Default = def, Options = opts }; }
    }

    /// <summary>Everything a widget needs while drawing. Coordinates are device pixels; use S() to scale design px.</summary>
    public class DrawCtx
    {
        public Graphics G;
        public Theme T;
        public Rectangle Bounds;      // tile rect (bar) or card rect (panel)
        public float Scale = 1f;      // DPI * bar-size scale. Design for M size @96dpi and wrap numbers in S().
        public bool Hover;            // mouse is over this tile/panel
        public Point Mouse = new Point(-1, -1);
        public bool IsPanel;
        public int BarHeight;         // full bar height in px
        /// <summary>True when the bar is vertical (docked left/right): tiles are stacked top→bottom and Bounds.Width is the
        /// sidebar's inner width. Widgets that can lay out as a grid (Apps, Tray) override Widget.MeasureHeight.</summary>
        public bool Vertical;
        /// <summary>The edge this tile's bar is docked to: top | bottom | left | right.</summary>
        public string Edge = "bottom";
        internal List<KeyValuePair<Rectangle, Action>> Hits = new List<KeyValuePair<Rectangle, Action>>();
        internal List<KeyValuePair<Rectangle, Action<int>>> Wheels = new List<KeyValuePair<Rectangle, Action<int>>>();
        internal Action<InputSpec> InputSink;

        public float S(float px) { return px * Scale; }
        public int Si(float px) { return (int)Math.Round(px * Scale); }
        /// <summary>Register a clickable region. Hits are tested last-registered-first; the whole tile falls back to Widget.OnClick.</summary>
        public void Hit(Rectangle r, Action onClick) { Hits.Add(new KeyValuePair<Rectangle, Action>(r, onClick)); }
        public void Hit(RectangleF r, Action onClick) { Hit(Rectangle.Round(r), onClick); }
        /// <summary>Register a mouse-wheel region (delta = +1 up / -1 down).</summary>
        public void Wheel(Rectangle r, Action<int> onWheel) { Wheels.Add(new KeyValuePair<Rectangle, Action<int>>(r, onWheel)); }
        public bool IsHover(RectangleF r) { return r.Contains(Mouse); }
        /// <summary>Panels only: place a real text box (for search/todos/notes/etc). Call every DrawPanel; it is kept by key.</summary>
        public void Input(string key, Rectangle r, string placeholder, Action<string> onSubmit, Action<string> onChange = null, bool multiline = false, string initial = null)
        {
            if (InputSink != null) InputSink(new InputSpec { Key = key, Rect = r, Placeholder = placeholder, OnSubmit = onSubmit, OnChange = onChange, Multiline = multiline, Initial = initial });
        }

        // convenience
        public Font F(float designPx, bool bold = false) { return Ui.Font(S(designPx), bold); }
        public Font FS(float designPx) { return Ui.FontSemi(S(designPx)); }
        public Font Mono(float designPx, bool bold = false) { return Ui.Mono(S(designPx), bold); }
        /// <summary>Standard pill button with hover state. Registers the hit.</summary>
        public void Button(RectangleF r, string label, Action onClick, bool primary = false, string icon = null)
        {
            bool h = IsHover(r);
            Ui.FillRound(G, r, Math.Min(r.Height / 2, S(10)), primary ? (h ? Ui.Mix(T.Accent, Color.White, 0.15) : T.Accent) : (h ? T.TileHover : T.Tile));
            var fg = primary ? Color.White : T.Text;
            if (icon != null && string.IsNullOrEmpty(label)) Ui.Icon(G, icon, r, fg, S(14));
            else if (icon != null)
            {
                var ir = new RectangleF(r.X + S(8), r.Y, S(16), r.Height); Ui.Icon(G, icon, ir, fg, S(13));
                Ui.Text(G, label, FS(12.5f), fg, new RectangleF(ir.Right + S(4), r.Y, r.Right - ir.Right - S(10), r.Height));
            }
            else Ui.Text(G, label, FS(12.5f), fg, r, 0);
            Hit(r, onClick);
        }
        /// <summary>Round icon button.</summary>
        public void IconButton(RectangleF r, string glyph, Action onClick, bool active = false, float iconPx = 14)
        {
            bool h = IsHover(r);
            var bg = active ? T.Accent : (h ? T.TileHover : Color.FromArgb(0, 0, 0, 0));
            if (bg.A > 0) Ui.FillRound(G, r, Math.Min(r.Width, r.Height) / 2, bg);
            Ui.Icon(G, glyph, r, active ? Color.White : T.Text, S(iconPx));
            Hit(r, onClick);
        }
        /// <summary>Card section title used at the top of panels.</summary>
        public float PanelHeader(string title, string subtitle = null, string icon = null)
        {
            var b = Bounds; float x = b.X + S(16), y = b.Y + S(14);
            if (icon != null) { Ui.Icon(G, icon, new RectangleF(x, y, S(18), S(18)), T.Accent, S(15)); x += S(24); }
            Ui.Text(G, title, FS(14), T.Text, new RectangleF(x, y, b.Right - x - S(16), S(18)));
            if (subtitle != null) Ui.Text(G, subtitle, F(11.5f), T.TextDim, new RectangleF(x, y, b.Right - x - S(16), S(18)), 1);
            return y + S(28);
        }
    }

    public class InputSpec { public string Key, Placeholder, Initial; public Rectangle Rect; public Action<string> OnSubmit, OnChange; public bool Multiline; }

    /// <summary>Host services the bar provides to widgets.</summary>
    public interface IWidgetHost
    {
        void Invalidate(Widget w);                 // thread-safe repaint request (coalesced)
        void Post(Action a);                       // run on UI thread
        void OpenPanel(Widget w);
        void ClosePanel();
        bool IsPanelOpen(Widget w);
        void OpenSettings(string page);            // opens the Settings app (e.g. "widgets", "accounts")
        Rectangle ScreenBoundsOf(Widget w);        // tile rect on screen
    }

    public abstract class Widget
    {
        public WidgetAttribute Meta;
        public WidgetInstance Instance;
        public IWidgetHost Host;
        public string InstId { get { return Instance != null ? Instance.Inst : Meta.Id; } }

        // ---- settings (per instance) ----
        public virtual SettingDef[] SettingDefs { get { return new SettingDef[0]; } }
        protected object Setting(string key)
        {
            object v;
            if (Instance != null && Instance.Settings != null && Instance.Settings.TryGetValue(key, out v) && v != null) return v;
            var d = SettingDefs.FirstOrDefault(x => x.Key == key); return d != null ? d.Default : null;
        }
        protected string SStr(string key) { return J.AsStr(Setting(key)); }
        protected double SNum(string key) { return J.AsNum(Setting(key)); }
        protected bool SBool(string key) { return J.AsBool(Setting(key)); }

        // ---- persisted state (todos, notes, laps...) ----
        Dictionary<string, object> state;
        protected Dictionary<string, object> State { get { if (state == null) state = StateStore.Load(StateKey); return state; } }
        protected virtual string StateKey { get { return InstId; } }  // override with Meta.Id to share across instances
        protected void SaveState() { StateStore.Save(StateKey, State); }

        // ---- lifecycle ----
        /// <summary>Called when the widget appears on the bar. Subscribe to Hub keys here.</summary>
        public virtual void OnStart() { }
        /// <summary>Called when removed / bar closes. Unsubscribe here.</summary>
        public virtual void OnStop() { }
        /// <summary>If > 0, the bar calls OnTick on the UI thread every RefreshMs (then repaints). Use for clocks/timers.</summary>
        public virtual int RefreshMs { get { return 0; } }
        public virtual void OnTick() { }

        // ---- bar tile ----
        /// <summary>Tile width in px for the current bar height. c.G is valid for text measurement.</summary>
        public abstract int Measure(DrawCtx c);
        /// <summary>Vertical bars only (c.Vertical, c.Bounds.Width = available width): the tile height this widget wants,
        /// or 0 to be a normal single row (bar tile height) drawn with its horizontal layout.</summary>
        public virtual int MeasureHeight(DrawCtx c) { return 0; }
        public abstract void Draw(DrawCtx c);
        /// <summary>Click on the tile where no Hit() region matched. Default: toggle panel.</summary>
        public virtual void OnClick(MouseButtons b) { if (b == MouseButtons.Left && PanelSize(1f) != Size.Empty) { if (Host.IsPanelOpen(this)) Host.ClosePanel(); else Host.OpenPanel(this); } }

        // ---- popup card ----
        /// <summary>Panel size in px at the given scale. Size.Empty = no panel.</summary>
        public virtual Size PanelSize(float scale) { return Size.Empty; }
        public virtual void DrawPanel(DrawCtx c) { }
        public virtual void OnPanelOpen() { }
        public virtual void OnPanelClose() { }

        public void Invalidate() { if (Host != null) Host.Invalidate(this); }

        // ---- right-click menu ----
        /// <summary>Return true when the widget handles a right-click at the current mouse position itself
        /// (e.g. an app icon in the task list, a tray icon). Otherwise the bar shows the standard widget menu.</summary>
        public virtual bool HandlesRightClick() { return false; }
        /// <summary>Widget-specific entries shown at the top of the standard right-click menu.</summary>
        public virtual void AddMenuItems(List<KeyValuePair<string, Action>> items) { }

        // ---- dragging inside a tile (e.g. reordering app icons) ----
        /// <summary>Mouse went down at p (bar client coords) and started dragging. Return true to own the drag
        /// (DragTo/EndDrag follow); false = the bar drags the whole tile as usual.</summary>
        public virtual bool BeginDrag(Point p) { return false; }
        public virtual void DragTo(Point p) { }
        public virtual void EndDrag(Point p, bool cancelled) { }

        /// <summary>Helper: when a widget needs an account that is not connected, show sample data plus this hint.</summary>
        protected bool HasAccount { get { return string.IsNullOrEmpty(Meta.Account) || Accounts.Connected(Meta.Account); } }
    }

    public static class WidgetRegistry
    {
        static Dictionary<string, Type> types;
        static Dictionary<string, WidgetAttribute> metas;
        static void Init()
        {
            if (types != null) return;
            types = new Dictionary<string, Type>(); metas = new Dictionary<string, WidgetAttribute>();
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (t.IsAbstract || !typeof(Widget).IsAssignableFrom(t)) continue;
                var a = (WidgetAttribute)t.GetCustomAttributes(typeof(WidgetAttribute), false).FirstOrDefault();
                if (a == null) continue;
                if (a.RefName == null) a.RefName = a.Name;
                types[a.Id] = t; metas[a.Id] = a;
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle); // lets widgets call Accounts.Define in static ctors
            }
        }
        public static IEnumerable<WidgetAttribute> All { get { Init(); return metas.Values.OrderBy(m => m.Category).ThenBy(m => m.Name); } }
        public static WidgetAttribute Meta(string id) { Init(); WidgetAttribute m; return metas.TryGetValue(id, out m) ? m : null; }
        public static Widget Create(string id, WidgetInstance inst)
        {
            Init(); Type t; if (!types.TryGetValue(id, out t)) return null;
            var w = (Widget)Activator.CreateInstance(t); w.Meta = metas[id]; w.Instance = inst ?? new WidgetInstance { Id = id, Inst = id };
            return w;
        }
    }

    /// <summary>
    /// Background data hub. Define a key once with a sampler; it only polls while at least one widget is subscribed.
    /// Samplers run on the thread pool; subscribers are invalidated when the value changes (by Equals / reference).
    /// </summary>
    public static class Hub
    {
        class Src { public int Interval; public Func<object> Sampler; public object Value; public HashSet<Widget> Subs = new HashSet<Widget>(); public System.Threading.Timer Timer; public int Busy; }
        static readonly Dictionary<string, Src> srcs = new Dictionary<string, Src>();

        /// <summary>Idempotent. intervalMs = poll period.</summary>
        public static void Define(string key, int intervalMs, Func<object> sampler)
        {
            lock (srcs) { if (!srcs.ContainsKey(key)) srcs[key] = new Src { Interval = intervalMs, Sampler = sampler }; }
        }
        public static void Subscribe(string key, Widget w)
        {
            lock (srcs)
            {
                Src s; if (!srcs.TryGetValue(key, out s)) return;
                s.Subs.Add(w);
                if (s.Timer == null) { var k = key; s.Timer = new System.Threading.Timer(_ => Run(k), null, 0, s.Interval); }
            }
        }
        public static void Unsubscribe(string key, Widget w)
        {
            lock (srcs)
            {
                Src s; if (!srcs.TryGetValue(key, out s)) return;
                s.Subs.Remove(w);
                if (s.Subs.Count == 0 && s.Timer != null) { s.Timer.Dispose(); s.Timer = null; }
            }
        }
        public static void UnsubscribeAll(Widget w) { lock (srcs) foreach (var k in srcs.Keys.ToList()) Unsubscribe(k, w); }
        /// <summary>Force an immediate re-sample (e.g. after a user action).</summary>
        public static void Refresh(string key) { ThreadPool.QueueUserWorkItem(_ => Run(key)); }
        public static object Get(string key) { lock (srcs) { Src s; return srcs.TryGetValue(key, out s) ? s.Value : null; } }
        public static T Get<T>(string key) where T : class { return Get(key) as T; }

        static void Run(string key)
        {
            Src s; lock (srcs) { if (!srcs.TryGetValue(key, out s)) return; }
            if (Interlocked.Exchange(ref s.Busy, 1) == 1) return;
            try
            {
                object v = null; try { v = s.Sampler(); } catch { }
                if (v == null) return;
                bool changed = !Equals(v, s.Value); s.Value = v;
                if (changed) { Widget[] subs; lock (srcs) subs = s.Subs.ToArray(); foreach (var w in subs) w.Invalidate(); }
            }
            finally { Interlocked.Exchange(ref s.Busy, 0); }
        }
    }
}
