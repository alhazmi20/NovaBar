// Calendar — iCal/ICS secret links (Google / Outlook / iCloud). Hub key "prod.calendar" (15 min). Shared with the Day Timeline.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NovaBar
{
    public class ProdCalEvent
    {
        public string Title, Location, Uid;
        public DateTime Start, End;   // local time
        public bool AllDay;
        public int Source;            // 1..3 = which link; 0 = sample
    }
    public class ProdCalSnap
    {
        public List<ProdCalEvent> Events = new List<ProdCalEvent>();
        public bool Sample; public string Error; public DateTime Fetched;
        public ProdCalEvent NextEvent(DateTime now, bool includeAllDay)
        {
            foreach (var e in Events)
            {
                if (e.AllDay && !includeAllDay) continue;
                if (e.End > now && (!e.AllDay || e.Start.Date >= now.Date)) return e;
            }
            return null;
        }
    }

    public static class ProdCal
    {
        public const string Key = "prod.calendar";
        static bool hooked;
        public static void Define()
        {
            Hub.Define(Key, 15 * 60 * 1000, Fetch);
            if (!hooked) { hooked = true; AppConfig.Changed += () => { var u = string.Join("|", Urls()); if (u != lastUrls) Hub.Refresh(Key); }; }
        }
        static string lastUrls = "";
        public static bool Connected { get { return !string.IsNullOrEmpty(Accounts.Get("calendar", "ical1")); } }
        static List<string> Urls()
        {
            var l = new List<string>();
            foreach (var f in new[] { "ical1", "ical2", "ical3" }) { var u = Accounts.Get("calendar", f); l.Add(string.IsNullOrEmpty(u) ? "" : u.Trim()); }
            return l;
        }

        static object Fetch()
        {
            var urls = Urls(); lastUrls = string.Join("|", urls);
            if (string.IsNullOrEmpty(urls[0])) return SampleSnap();
            var snap = new ProdCalSnap { Fetched = DateTime.Now };
            DateTime from = DateTime.Today.AddDays(-1), to = DateTime.Today.AddDays(15);
            int ok = 0;
            for (int i = 0; i < urls.Count; i++)
            {
                var u = urls[i]; if (u.Length == 0) continue;
                if (u.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u.Substring(9);
                var body = Http.Request("GET", u, null, null, 20000, "Accept: text/calendar, */*");
                if (body == null || body.IndexOf("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) < 0) { snap.Error = "Couldn't load calendar " + (i + 1); continue; }
                ok++;
                try { snap.Events.AddRange(ProdIcs.Parse(body, from, to, i + 1)); } catch { snap.Error = "Couldn't read calendar " + (i + 1); }
            }
            if (ok == 0)
            {   // keep the last good data on network failure
                var prev = Hub.Get<ProdCalSnap>(Key);
                if (prev != null && !prev.Sample) { prev.Error = snap.Error; return prev; }
            }
            snap.Events = snap.Events.OrderBy(e => e.Start).ThenBy(e => e.AllDay ? 0 : 1).ToList();
            return snap;
        }

        public static ProdCalSnap SampleSnap()
        {
            var t = DateTime.Today; var s = new ProdCalSnap { Sample = true, Fetched = DateTime.Now };
            Action<int, double, double, string, string> add = (day, h, dur, title, loc) =>
                s.Events.Add(new ProdCalEvent { Title = title, Location = loc, Start = t.AddDays(day).AddHours(h), End = t.AddDays(day).AddHours(h + dur), Source = 0 });
            add(0, 9.5, 0.25, "Daily standup", "Google Meet");
            add(0, 11, 1, "Design review", "Room 4B");
            add(0, 14, 0.5, "1:1 with Sara", "");
            add(0, 16.5, 1, "Sprint planning", "Zoom");
            add(0, 20, 1.5, "Dinner with Omar", "Najd Village");
            add(1, 9.5, 0.25, "Daily standup", "Google Meet");
            add(1, 13, 1, "Customer call · Acme", "Teams");
            add(2, 10, 2, "Workshop: Q1 roadmap", "HQ");
            add(3, 9.5, 0.25, "Daily standup", "Google Meet");
            add(4, 18, 1, "Gym", "");
            s.Events.Add(new ProdCalEvent { Title = "Domain renewal · novabar.app", Start = t.AddDays(2), End = t.AddDays(3), AllDay = true });
            s.Events.Add(new ProdCalEvent { Title = "Stripe payout", Start = t.AddDays(3), End = t.AddDays(4), AllDay = true });
            // keep it alive in the evening: if all of today's sample events are over, nothing breaks — later days remain
            s.Events = s.Events.OrderBy(e => e.Start).ToList();
            return s;
        }
    }

    /// <summary>Minimal RFC 5545 reader: VEVENT with DTSTART/DTEND/DURATION (TZID, UTC, all-day), SUMMARY, LOCATION, RRULE (DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY), EXDATE, RECURRENCE-ID overrides.</summary>
    public static class ProdIcs
    {
        class Prop { public string Name, Value; public Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        class Raw { public List<Prop> Props = new List<Prop>(); public Prop Get(string n) { return Props.FirstOrDefault(p => p.Name == n); } }
        struct Dt { public DateTime Wall; public TimeZoneInfo Tz; public bool Utc, DateOnly, Ok; }

        public static List<ProdCalEvent> Parse(string ics, DateTime from, DateTime to, int source)
        {
            var lines = Unfold(ics);
            var events = new List<Raw>(); var tzOffsets = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            Raw cur = null; string tzId = null; bool inStd = false; int depth = 0;
            foreach (var line in lines)
            {
                var p = ParseLine(line); if (p == null) continue;
                if (p.Name == "BEGIN")
                {
                    var v = p.Value.ToUpperInvariant();
                    if (v == "VEVENT" && cur == null) { cur = new Raw(); depth = 0; }
                    else if (cur != null) depth++;
                    else if (v == "VTIMEZONE") tzId = "";
                    else if (v == "STANDARD" && tzId != null) inStd = true;
                    continue;
                }
                if (p.Name == "END")
                {
                    var v = p.Value.ToUpperInvariant();
                    if (cur != null) { if (depth > 0) depth--; else if (v == "VEVENT") { events.Add(cur); cur = null; } }
                    else if (v == "VTIMEZONE") tzId = null;
                    else if (v == "STANDARD") inStd = false;
                    continue;
                }
                if (cur != null) { if (depth == 0) cur.Props.Add(p); }
                else if (tzId != null)
                {
                    if (p.Name == "TZID") tzId = p.Value;
                    else if (inStd && p.Name == "TZOFFSETTO" && tzId.Length > 0) { TimeSpan off; if (TryOffset(p.Value, out off)) tzOffsets[tzId] = off; }
                }
            }

            var result = new List<ProdCalEvent>();
            // overrides: UID -> set of original start (as local) replaced by a RECURRENCE-ID instance
            var overrides = new Dictionary<string, HashSet<DateTime>>();
            foreach (var e in events)
            {
                var rid = e.Get("RECURRENCE-ID"); var uid = e.Get("UID");
                if (rid == null || uid == null) continue;
                var d = ParseDt(rid, tzOffsets); if (!d.Ok) continue;
                HashSet<DateTime> hs; if (!overrides.TryGetValue(uid.Value, out hs)) overrides[uid.Value] = hs = new HashSet<DateTime>();
                hs.Add(ToLocal(d));
            }
            foreach (var e in events)
            {
                var stP = e.Get("DTSTART"); if (stP == null) continue;
                var status = e.Get("STATUS"); if (status != null && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
                var st = ParseDt(stP, tzOffsets); if (!st.Ok) continue;
                TimeSpan dur;
                var enP = e.Get("DTEND"); var duP = e.Get("DURATION");
                if (enP != null) { var en = ParseDt(enP, tzOffsets); dur = en.Ok ? (st.DateOnly ? en.Wall - st.Wall : ToUtc(en) - ToUtc(st)) : TimeSpan.Zero; }
                else if (duP != null) dur = ParseDuration(duP.Value);
                else dur = st.DateOnly ? TimeSpan.FromDays(1) : TimeSpan.Zero;
                if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                string title = Unescape(e.Get("SUMMARY") != null ? e.Get("SUMMARY").Value : "(No title)");
                string loc = e.Get("LOCATION") != null ? Unescape(e.Get("LOCATION").Value) : "";
                string uid = e.Get("UID") != null ? e.Get("UID").Value : Guid.NewGuid().ToString();
                var rrule = e.Get("RRULE");
                bool isOverride = e.Get("RECURRENCE-ID") != null;
                var ex = new HashSet<DateTime>();
                foreach (var xp in e.Props.Where(p => p.Name == "EXDATE"))
                    foreach (var part in xp.Value.Split(','))
                    {
                        var xpp = new Prop { Name = "EXDATE", Value = part.Trim(), Params = xp.Params };
                        var xd = ParseDt(xpp, tzOffsets); if (xd.Ok) ex.Add(xd.DateOnly ? xd.Wall.Date : ToLocal(xd));
                    }
                HashSet<DateTime> ov; overrides.TryGetValue(uid, out ov);

                Func<Dt, ProdCalEvent> make = occ =>
                {
                    DateTime s, en;
                    if (occ.DateOnly) { s = occ.Wall.Date; en = s + (dur.TotalDays >= 1 ? TimeSpan.FromDays(Math.Round(dur.TotalDays)) : TimeSpan.FromDays(1)); }
                    else { s = ToLocal(occ); en = s + dur; }
                    return new ProdCalEvent { Title = title, Location = loc, Uid = uid, Start = s, End = en, AllDay = occ.DateOnly, Source = source };
                };
                if (rrule == null || isOverride)
                {
                    var ev = make(st);
                    if (ev.End >= from && ev.Start <= to) result.Add(ev);
                    continue;
                }
                foreach (var occWall in Expand(st.Wall, rrule.Value, to.AddDays(1), tzOffsets))
                {
                    var occ = st; occ.Wall = occWall;
                    var ev = make(occ);
                    if (ev.End < from) continue;
                    if (ev.Start > to) break;
                    if (ex.Contains(occ.DateOnly ? ev.Start.Date : ev.Start)) continue;
                    if (ov != null && ov.Contains(ev.Start)) continue;
                    result.Add(ev);
                }
            }
            return result;
        }

        // ---------- RRULE ----------
        static readonly string[] DayCodes = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };
        /// <summary>Yields occurrence wall-clock start times (in the event's own zone) in ascending order, up to `limit` (wall).</summary>
        static IEnumerable<DateTime> Expand(DateTime start, string rule, DateTime limit, Dictionary<string, TimeSpan> tzo)
        {
            var r = rule.Split(';').Select(x => x.Split(new[] { '=' }, 2)).Where(x => x.Length == 2).ToDictionary(x => x[0].Trim().ToUpperInvariant(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);
            string freq; r.TryGetValue("FREQ", out freq); freq = (freq ?? "").ToUpperInvariant();
            int interval = 1; string sv; if (r.TryGetValue("INTERVAL", out sv)) int.TryParse(sv, out interval); if (interval < 1) interval = 1;
            int count = int.MaxValue; if (r.TryGetValue("COUNT", out sv)) int.TryParse(sv, out count);
            DateTime until = DateTime.MaxValue;
            if (r.TryGetValue("UNTIL", out sv))
            {
                var d = ParseDt(new Prop { Name = "UNTIL", Value = sv }, tzo);
                if (d.Ok) until = d.DateOnly ? d.Wall.Date.AddDays(1).AddTicks(-1) : (d.Utc ? d.Wall.ToLocalTime() : d.Wall);
            }
            var byday = new List<KeyValuePair<int, DayOfWeek>>(); // (ordinal or 0, day)
            if (r.TryGetValue("BYDAY", out sv))
                foreach (var tok in sv.Split(','))
                {
                    var t = tok.Trim().ToUpperInvariant(); if (t.Length < 2) continue;
                    int di = Array.IndexOf(DayCodes, t.Substring(t.Length - 2)); if (di < 0) continue;
                    int ord = 0; if (t.Length > 2) int.TryParse(t.Substring(0, t.Length - 2), out ord);
                    byday.Add(new KeyValuePair<int, DayOfWeek>(ord, (DayOfWeek)di));
                }
            var bymd = new List<int>();
            if (r.TryGetValue("BYMONTHDAY", out sv)) foreach (var tok in sv.Split(',')) { int v; if (int.TryParse(tok.Trim(), out v)) bymd.Add(v); }
            var bymonth = new List<int>();
            if (r.TryGetValue("BYMONTH", out sv)) foreach (var tok in sv.Split(',')) { int v; if (int.TryParse(tok.Trim(), out v)) bymonth.Add(v); }

            int n = 0, guard = 0; var tod = start.TimeOfDay;
            if (freq == "DAILY")
            {
                for (var d = start; guard++ < 20000; d = d.AddDays(interval))
                {
                    if (d > until || d > limit || n >= count) yield break;
                    if (byday.Count > 0 && !byday.Any(b => b.Value == d.DayOfWeek)) continue;
                    if (bymonth.Count > 0 && !bymonth.Contains(d.Month)) continue;
                    n++; yield return d;
                }
            }
            else if (freq == "WEEKLY")
            {
                var days = byday.Count > 0 ? byday.Select(b => b.Value).Distinct().ToList() : new List<DayOfWeek> { start.DayOfWeek };
                // week starts Monday (WKST default MO)
                Func<DayOfWeek, int> off = dw => ((int)dw + 6) % 7;
                days = days.OrderBy(off).ToList();
                var weekStart = start.Date.AddDays(-off(start.DayOfWeek));
                for (int w = 0; guard++ < 5000; w += interval)
                {
                    var ws = weekStart.AddDays(7 * w);
                    if (ws > limit || ws > until) yield break;
                    foreach (var dw in days)
                    {
                        var d = ws.AddDays(off(dw)) + tod;
                        if (d < start) continue;
                        if (d > until || d > limit || n >= count) yield break;
                        n++; yield return d;
                    }
                }
            }
            else if (freq == "MONTHLY" || freq == "YEARLY")
            {
                bool yearly = freq == "YEARLY";
                var baseM = new DateTime(start.Year, start.Month, 1);
                for (int k = 0; guard++ < 3000; k += interval)
                {
                    var months = new List<DateTime>();
                    if (yearly)
                    {
                        var y = baseM.AddYears(k);
                        if (bymonth.Count > 0) foreach (var m in bymonth.OrderBy(x => x)) months.Add(new DateTime(y.Year, Math.Max(1, Math.Min(12, m)), 1));
                        else months.Add(new DateTime(y.Year, start.Month, 1));
                    }
                    else months.Add(baseM.AddMonths(k));
                    if (months[0] > limit || months[0] > until) yield break;
                    foreach (var m in months)
                    {
                        var cands = new List<DateTime>();
                        int dim = DateTime.DaysInMonth(m.Year, m.Month);
                        if (byday.Count > 0)
                        {
                            foreach (var b in byday)
                            {
                                var all = Enumerable.Range(1, dim).Select(x => new DateTime(m.Year, m.Month, x)).Where(x => x.DayOfWeek == b.Value).ToList();
                                if (b.Key == 0) cands.AddRange(all);
                                else if (b.Key > 0 && b.Key <= all.Count) cands.Add(all[b.Key - 1]);
                                else if (b.Key < 0 && -b.Key <= all.Count) cands.Add(all[all.Count + b.Key]);
                            }
                            if (bymd.Count > 0) cands = cands.Where(x => bymd.Contains(x.Day) || bymd.Contains(x.Day - dim - 1)).ToList();
                        }
                        else if (bymd.Count > 0)
                        { foreach (var md in bymd) { int dd = md > 0 ? md : dim + md + 1; if (dd >= 1 && dd <= dim) cands.Add(new DateTime(m.Year, m.Month, dd)); } }
                        else if (start.Day <= dim) cands.Add(new DateTime(m.Year, m.Month, start.Day));
                        foreach (var cd in cands.Distinct().OrderBy(x => x))
                        {
                            var d = cd + tod;
                            if (d < start) continue;
                            if (d > until || d > limit || n >= count) yield break;
                            n++; yield return d;
                        }
                    }
                }
            }
            else yield return start; // unsupported FREQ (HOURLY etc.) → just the first instance
        }

        // ---------- primitives ----------
        static List<string> Unfold(string s)
        {
            var outL = new List<string>(); var sb = new StringBuilder();
            foreach (var raw in s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { sb.Append(raw, 1, raw.Length - 1); continue; }
                if (sb.Length > 0) outL.Add(sb.ToString());
                sb.Clear(); sb.Append(raw);
            }
            if (sb.Length > 0) outL.Add(sb.ToString());
            return outL;
        }
        static Prop ParseLine(string line)
        {
            // name *(;param=value) : value  — colon inside quoted params is allowed
            bool q = false; int colon = -1;
            for (int i = 0; i < line.Length; i++) { char ch = line[i]; if (ch == '"') q = !q; else if (ch == ':' && !q) { colon = i; break; } }
            if (colon <= 0) return null;
            var head = line.Substring(0, colon); var p = new Prop { Value = line.Substring(colon + 1) };
            var parts = SplitParams(head);
            p.Name = parts[0].ToUpperInvariant();
            for (int i = 1; i < parts.Count; i++)
            {
                int eq = parts[i].IndexOf('='); if (eq <= 0) continue;
                p.Params[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1).Trim('"');
            }
            return p;
        }
        static List<string> SplitParams(string s)
        {
            var l = new List<string>(); var sb = new StringBuilder(); bool q = false;
            foreach (var ch in s) { if (ch == '"') q = !q; if (ch == ';' && !q) { l.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch); }
            l.Add(sb.ToString()); return l;
        }
        static string Unescape(string s) { return (s ?? "").Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim(); }

        static Dt ParseDt(Prop p, Dictionary<string, TimeSpan> tzo)
        {
            var d = new Dt(); var v = (p.Value ?? "").Trim();
            string vt; p.Params.TryGetValue("VALUE", out vt);
            DateTime t;
            if ((vt != null && vt.Equals("DATE", StringComparison.OrdinalIgnoreCase)) || v.Length == 8)
            {
                if (DateTime.TryParseExact(v.Substring(0, Math.Min(8, v.Length)), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) { d.Wall = t; d.DateOnly = true; d.Ok = true; }
                return d;
            }
            bool utc = v.EndsWith("Z", StringComparison.OrdinalIgnoreCase); if (utc) v = v.Substring(0, v.Length - 1);
            if (!DateTime.TryParseExact(v, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return d;
            d.Wall = t; d.Utc = utc; d.Ok = true;
            string tzid; if (!utc && p.Params.TryGetValue("TZID", out tzid)) d.Tz = FindTz(tzid, tzo);
            return d;
        }
        static DateTime ToUtc(Dt d)
        {
            if (d.DateOnly) return DateTime.SpecifyKind(d.Wall, DateTimeKind.Local).ToUniversalTime();
            if (d.Utc) return DateTime.SpecifyKind(d.Wall, DateTimeKind.Utc);
            if (d.Tz != null)
            {
                var w = DateTime.SpecifyKind(d.Wall, DateTimeKind.Unspecified);
                try { if (d.Tz.IsInvalidTime(w)) w = w.AddHours(1); return TimeZoneInfo.ConvertTimeToUtc(w, d.Tz); } catch { }
            }
            return DateTime.SpecifyKind(d.Wall, DateTimeKind.Local).ToUniversalTime(); // floating time = local
        }
        static DateTime ToLocal(Dt d) { return d.DateOnly ? d.Wall.Date : DateTime.SpecifyKind(ToUtc(d).ToLocalTime(), DateTimeKind.Unspecified); }

        static TimeSpan ParseDuration(string s)
        {
            // P[n]W | P[n]DT[n]H[n]M[n]S, optional sign
            var ts = TimeSpan.Zero; bool neg = s.StartsWith("-"); s = s.TrimStart('+', '-').ToUpperInvariant();
            int num = 0; bool inT = false;
            foreach (var ch in s)
            {
                if (char.IsDigit(ch)) { num = num * 10 + (ch - '0'); continue; }
                switch (ch)
                {
                    case 'T': inT = true; break;
                    case 'W': ts += TimeSpan.FromDays(7 * num); break;
                    case 'D': ts += TimeSpan.FromDays(num); break;
                    case 'H': ts += TimeSpan.FromHours(num); break;
                    case 'M': ts += inT ? TimeSpan.FromMinutes(num) : TimeSpan.Zero; break;
                    case 'S': ts += TimeSpan.FromSeconds(num); break;
                }
                num = 0;
            }
            return neg ? -ts : ts;
        }
        static bool TryOffset(string s, out TimeSpan off)
        {
            off = TimeSpan.Zero; s = s.Trim(); if (s.Length < 5) return false;
            int sign = s[0] == '-' ? -1 : 1; int h, m;
            if (!int.TryParse(s.Substring(1, 2), out h) || !int.TryParse(s.Substring(3, 2), out m)) return false;
            off = TimeSpan.FromMinutes(sign * (h * 60 + m)); return true;
        }

        static readonly Dictionary<string, TimeZoneInfo> tzCache = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> Iana = BuildIana();
        static Dictionary<string, string> BuildIana()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            const string map =
                "America/New_York,America/Detroit,America/Toronto,America/Nassau,US/Eastern=Eastern Standard Time|America/Chicago,America/Winnipeg,US/Central=Central Standard Time|" +
                "America/Denver,America/Edmonton,US/Mountain=Mountain Standard Time|America/Phoenix=US Mountain Standard Time|America/Los_Angeles,America/Vancouver,US/Pacific=Pacific Standard Time|" +
                "America/Anchorage=Alaskan Standard Time|Pacific/Honolulu=Hawaiian Standard Time|America/Halifax=Atlantic Standard Time|America/St_Johns=Newfoundland Standard Time|" +
                "America/Mexico_City=Central Standard Time (Mexico)|America/Sao_Paulo=E. South America Standard Time|America/Argentina/Buenos_Aires,America/Buenos_Aires=Argentina Standard Time|" +
                "America/Bogota,America/Lima=SA Pacific Standard Time|America/Santiago=Pacific SA Standard Time|America/Caracas=Venezuela Standard Time|" +
                "Europe/London,Europe/Dublin,Europe/Lisbon=GMT Standard Time|Atlantic/Reykjavik,Africa/Abidjan,Africa/Accra=Greenwich Standard Time|" +
                "Europe/Paris,Europe/Madrid,Europe/Brussels,Europe/Copenhagen=Romance Standard Time|Europe/Berlin,Europe/Amsterdam,Europe/Rome,Europe/Zurich,Europe/Vienna,Europe/Stockholm,Europe/Oslo,Europe/Luxembourg,Europe/Monaco,Europe/Malta=W. Europe Standard Time|" +
                "Europe/Warsaw,Europe/Zagreb,Europe/Sarajevo,Europe/Skopje=Central European Standard Time|Europe/Prague,Europe/Budapest,Europe/Belgrade,Europe/Bratislava,Europe/Ljubljana=Central Europe Standard Time|" +
                "Europe/Athens,Europe/Bucharest=GTB Standard Time|Europe/Helsinki,Europe/Kiev,Europe/Kyiv,Europe/Riga,Europe/Tallinn,Europe/Vilnius,Europe/Sofia=FLE Standard Time|" +
                "Europe/Istanbul,Asia/Istanbul=Turkey Standard Time|Europe/Moscow=Russian Standard Time|Africa/Cairo=Egypt Standard Time|Africa/Johannesburg=South Africa Standard Time|" +
                "Africa/Lagos,Africa/Algiers=W. Central Africa Standard Time|Africa/Nairobi,Africa/Addis_Ababa=E. Africa Standard Time|Africa/Casablanca=Morocco Standard Time|" +
                "Asia/Jerusalem,Asia/Tel_Aviv=Israel Standard Time|Asia/Amman=Jordan Standard Time|Asia/Beirut=Middle East Standard Time|Asia/Damascus=Syria Standard Time|" +
                "Asia/Riyadh,Asia/Kuwait,Asia/Qatar,Asia/Bahrain,Asia/Aden=Arab Standard Time|Asia/Baghdad=Arabic Standard Time|Asia/Dubai,Asia/Muscat=Arabian Standard Time|Asia/Tehran=Iran Standard Time|" +
                "Asia/Karachi=Pakistan Standard Time|Asia/Kolkata,Asia/Calcutta=India Standard Time|Asia/Kathmandu=Nepal Standard Time|Asia/Dhaka=Bangladesh Standard Time|Asia/Tashkent=West Asia Standard Time|" +
                "Asia/Bangkok,Asia/Jakarta,Asia/Ho_Chi_Minh,Asia/Saigon=SE Asia Standard Time|Asia/Singapore,Asia/Kuala_Lumpur,Asia/Manila=Singapore Standard Time|" +
                "Asia/Shanghai,Asia/Hong_Kong,Asia/Chongqing,Asia/Macau=China Standard Time|Asia/Taipei=Taipei Standard Time|Asia/Tokyo=Tokyo Standard Time|Asia/Seoul=Korea Standard Time|" +
                "Australia/Sydney,Australia/Melbourne,Australia/Canberra=AUS Eastern Standard Time|Australia/Brisbane=E. Australia Standard Time|Australia/Adelaide=Cen. Australia Standard Time|" +
                "Australia/Darwin=AUS Central Standard Time|Australia/Perth=W. Australia Standard Time|Pacific/Auckland=New Zealand Standard Time|" +
                "UTC,Etc/UTC,Etc/GMT,GMT,Z=UTC";
            foreach (var grp in map.Split('|')) { var kv = grp.Split('='); foreach (var k in kv[0].Split(',')) d[k] = kv[1]; }
            return d;
        }
        static TimeZoneInfo FindTz(string id, Dictionary<string, TimeSpan> tzo)
        {
            if (string.IsNullOrEmpty(id)) return null;
            id = id.Trim().Trim('"'); if (id.StartsWith("/")) id = id.TrimStart('/');
            TimeZoneInfo tz;
            lock (tzCache) if (tzCache.TryGetValue(id, out tz)) return tz;
            tz = null;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            if (tz == null) { string w; if (Iana.TryGetValue(id, out w)) try { tz = TimeZoneInfo.FindSystemTimeZoneById(w); } catch { } }
            if (tz == null) foreach (var z in TimeZoneInfo.GetSystemTimeZones()) if (id.IndexOf(z.StandardName, StringComparison.OrdinalIgnoreCase) >= 0 || z.DisplayName.Equals(id, StringComparison.OrdinalIgnoreCase)) { tz = z; break; }
            if (tz == null) { TimeSpan off; if (tzo.TryGetValue(id, out off)) try { tz = TimeZoneInfo.CreateCustomTimeZone(id, off, id, id); } catch { } }
            lock (tzCache) tzCache[id] = tz;
            return tz;
        }
    }

    [Widget("calendar", "Calendar", "Productivity", "Your next event and how soon it starts. Google, Outlook and iCloud events sit next to renewals and payouts.", RefName = "Calendar", Account = "calendar", Data = DataKind.NeedsAccount)]
    public class ProdCalendarWidget : Widget
    {
        static ProdCalendarWidget()
        {
            Accounts.Define("calendar", "Calendar (iCal links)",
                "Paste the secret iCal/ICS address of up to three calendars. Google Calendar: Settings → your calendar → Integrate calendar → \"Secret address in iCal format\". Outlook.com: Settings → Calendar → Shared calendars → Publish a calendar → ICS link. iCloud: share calendar as Public and copy the webcal:// link. Only the first link is required.",
                "https://support.google.com/calendar/answer/37648",
                Accounts.F("ical1", "iCal link 1", true, "https://calendar.google.com/calendar/ical/…/basic.ics"),
                Accounts.F("ical2", "iCal link 2 (optional)", true, "webcal://p01-caldav.icloud.com/…"),
                Accounts.F("ical3", "iCal link 3 (optional)", true, "https://outlook.office365.com/owa/calendar/…/calendar.ics"));
        }
        int scroll;
        public override void OnStart() { ProdCal.Define(); Hub.Subscribe(ProdCal.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(ProdCal.Key, this); }
        public override int RefreshMs { get { return 30000; } }

        ProdCalSnap Snap { get { var s = Hub.Get<ProdCalSnap>(ProdCal.Key); if (s == null && !ProdCal.Connected) return ProdCal.SampleSnap(); return s; } }
        static Color SourceColor(Theme t, int src) { return src <= 1 ? t.Accent : src == 2 ? t.Series[2] : t.Series[4]; }

        public override int Measure(DrawCtx c) { return c.Si(186); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G; var s = Snap; var now = DateTime.Now;
            // date "badge" icon
            var ic = new RectangleF(b.X + c.S(8), b.Y + (b.Height - c.S(24)) / 2, c.S(22), c.S(24));
            Ui.FillRound(g, ic, c.S(5), c.T.Track);
            Ui.FillRound(g, new RectangleF(ic.X, ic.Y, ic.Width, c.S(7)), c.S(3), c.T.Bad);
            g.FillRectangle(Ui.B(c.T.Bad), ic.X, ic.Y + c.S(4), ic.Width, c.S(3));
            Ui.Text(g, now.Day.ToString(), c.FS(11), c.T.Text, new RectangleF(ic.X, ic.Y + c.S(7), ic.Width, ic.Height - c.S(7)), 0);
            float x = ic.Right + c.S(8), w = b.Right - c.S(10) - x;
            var e = s == null ? null : s.NextEvent(now, false);
            if (s == null) { Ui.Text(g, "Loading…", c.F(12), c.T.TextDim, new RectangleF(x, b.Y, w, b.Height)); return; }
            if (e == null || e.Start.Date > now.Date.AddDays(1))
            {
                Ui.Text(g, "No more events", c.FS(12.5f), c.T.Text, new RectangleF(x, b.Y + c.S(3), w, c.S(17)));
                Ui.Text(g, e == null ? "next 2 weeks free" : "next: " + e.Start.ToString("ddd", ProdUtil.Inv) + " " + Ui.Clock(e.Start), c.F(10), c.T.TextDim, new RectangleF(x, b.Y + c.S(19), w, c.S(13)));
                return;
            }
            Ui.Text(g, e.Title, c.FS(12.5f), c.T.Text, new RectangleF(x, b.Y + c.S(3), w, c.S(17)));
            bool live = e.Start <= now;
            string sub = live ? "now · " + ProdUtil.Dur((e.End - now).TotalSeconds + 59) + " left" : (e.Start.Date == now.Date ? ProdUtil.Until(e.Start) + " · " + Ui.Clock(e.Start) : "tomorrow " + Ui.Clock(e.Start));
            bool soon = !live && (e.Start - now).TotalMinutes <= 15;
            Ui.Text(g, sub, c.F(10), live ? c.T.Good : soon ? c.T.Warn : c.T.TextDim, new RectangleF(x, b.Y + c.S(19), w, c.S(13)));
        }

        public override Size PanelSize(float s) { return new Size((int)(350 * s), (int)(420 * s)); }
        public override void OnPanelOpen() { scroll = 0; }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G; var s = Snap; var now = DateTime.Now;
            c.PanelHeader("Calendar", s == null || s.Sample ? null : now.ToString("dddd, MMMM d", ProdUtil.Inv), Icons.Calendar);
            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(46);
            bool sample = s == null || s.Sample;
            float bottom = b.Bottom - c.S(10);
            if (sample)
            {
                bottom = b.Bottom - c.S(52);
                var cb = new RectangleF(x, b.Bottom - c.S(44), w, c.S(32));
                c.Button(cb, "Connect in Settings", () => Host.OpenSettings("accounts"), true, Icons.Link);
                ProdUtil.SampleBadge(c, b.Right - c.S(16), b.Y + c.S(23));
            }
            else if (s.Error != null) Ui.Text(g, s.Error, c.F(10), c.T.Warn, new RectangleF(x, b.Y + c.S(14), w, c.S(18)), 1);
            if (s == null) { Ui.Text(g, "Loading…", c.F(12), c.T.TextDim, new RectangleF(x, y, w, c.S(20))); return; }

            // flatten into rows: day headers + events (next 14 days)
            var rows = new List<object>();
            DateTime lastDay = DateTime.MinValue;
            foreach (var e in s.Events)
            {
                if (e.End <= now && !(e.AllDay && e.Start.Date == now.Date)) continue;
                if (e.Start.Date > now.Date.AddDays(14)) break;
                var day = e.Start.Date < now.Date ? now.Date : e.Start.Date;
                if (day != lastDay) { rows.Add(day); lastDay = day; }
                rows.Add(e);
            }
            if (rows.Count == 0 || !(rows[0] is DateTime) || (DateTime)rows[0] != now.Date) { rows.Insert(0, "Nothing else today"); rows.Insert(0, now.Date); }
            float hh = c.S(26), eh = c.S(42);
            Func<object, float> hOf = o => o is DateTime ? hh : o is string ? c.S(26) : eh;
            // scroll by rows
            float avail = bottom - y; int maxScroll = 0; { float acc = 0; for (int i = rows.Count - 1; i >= 0; i--) { acc += hOf(rows[i]); if (acc > avail) { maxScroll = i + 1; break; } } }
            scroll = Math.Max(0, Math.Min(scroll, maxScroll));
            c.Wheel(new Rectangle((int)b.X, (int)y, b.Width, (int)avail), d => { scroll -= d; Invalidate(); });
            var oldClip = g.Clip; g.SetClip(new RectangleF(b.X, y, b.Width, avail));
            for (int i = scroll; i < rows.Count && y < bottom; i++)
            {
                var o = rows[i];
                if (o is DateTime)
                {
                    var d = (DateTime)o;
                    Ui.Text(g, ProdUtil.DayLabel(d).ToUpperInvariant(), c.FS(9.5f), d == now.Date ? c.T.Accent : c.T.TextFaint, new RectangleF(x, y + c.S(6), w, c.S(16)));
                    y += hh; continue;
                }
                if (o is string) { Ui.Text(g, (string)o, c.F(12), c.T.TextFaint, new RectangleF(x, y, w, c.S(22))); y += c.S(26); continue; }
                var e = (ProdCalEvent)o; var col = SourceColor(c.T, e.Source);
                bool live = !e.AllDay && e.Start <= now && e.End > now;
                var rr = new RectangleF(x - c.S(6), y, w + c.S(12), eh - c.S(4));
                if (live) Ui.FillRound(g, rr, c.S(8), Color.FromArgb(c.T.Dark ? 40 : 30, c.T.Good));
                else if (c.IsHover(rr)) Ui.FillRound(g, rr, c.S(8), c.T.Tile);
                Ui.FillRound(g, new RectangleF(x, y + c.S(5), c.S(3.5f), eh - c.S(14)), c.S(1.75f), e.AllDay ? Ui.WithA(col, 140) : col);
                float tx = x + c.S(12), timeW = c.S(64);
                string t1 = e.AllDay ? "All day" : Ui.Clock(e.Start), t2 = e.AllDay ? "" : Ui.Clock(e.End);
                Ui.Text(g, t1, c.FS(11.5f), c.T.Text, new RectangleF(tx, y + c.S(4), timeW, c.S(16)));
                Ui.Text(g, t2, c.F(10.5f), c.T.TextFaint, new RectangleF(tx, y + c.S(20), timeW, c.S(14)));
                float bx = x + w;
                string badge = null; Color bc = c.T.Accent;
                if (live) { badge = "Now"; bc = c.T.Good; }
                else if (!e.AllDay && e.Start > now && (e.Start - now).TotalHours < 12 && e.Start.Date == now.Date) { badge = ProdUtil.Until(e.Start); bc = (e.Start - now).TotalMinutes <= 15 ? c.T.Warn : c.T.Accent; }
                if (badge != null) bx = ProdUtil.Badge(c, badge, x + w, y + c.S(12), bc, Color.FromArgb(c.T.Dark ? 45 : 35, bc), 9.5f) - c.S(6);
                Ui.Text(g, e.Title, c.FS(12.5f), c.T.Text, new RectangleF(tx + timeW, y + c.S(3), bx - tx - timeW, c.S(18)));
                if (!string.IsNullOrEmpty(e.Location)) Ui.Text(g, e.Location, c.F(10.5f), c.T.TextDim, new RectangleF(tx + timeW, y + c.S(20), x + w - tx - timeW, c.S(14)));
                else if (!e.AllDay) Ui.Text(g, ProdUtil.Dur((e.End - e.Start).TotalSeconds), c.F(10.5f), c.T.TextFaint, new RectangleF(tx + timeW, y + c.S(20), x + w - tx - timeW, c.S(14)));
                y += eh;
            }
            g.Clip = oldClip;
        }
    }
}
