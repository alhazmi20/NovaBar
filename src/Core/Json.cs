// Tiny JSON helpers over JavaScriptSerializer (System.Web.Extensions, ships with .NET Framework).
// Parsed objects are Dictionary<string,object>, object[] (arrays), string, int/long/decimal, bool, null.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace NovaBar
{
    public static class J
    {
        static JavaScriptSerializer Ser() { var s = new JavaScriptSerializer(); s.MaxJsonLength = int.MaxValue; s.RecursionLimit = 256; return s; }

        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return Ser().DeserializeObject(s); } catch { return null; }
        }
        public static Dictionary<string, object> ParseObj(string s) { return Parse(s) as Dictionary<string, object>; }

        public static string Stringify(object o, bool pretty = false)
        {
            var s = Ser().Serialize(o);
            return pretty ? Pretty(s) : s;
        }

        /// <summary>Path getter: J.Get(obj, "data.items.0.name"). Returns null if missing.</summary>
        public static object Get(object o, string path)
        {
            if (o == null || string.IsNullOrEmpty(path)) return o;
            foreach (var part in path.Split('.'))
            {
                if (o == null) return null;
                var d = o as IDictionary<string, object>;
                if (d != null) { object v; if (!d.TryGetValue(part, out v)) return null; o = v; continue; }
                var l = o as IList;
                int idx;
                if (l != null && int.TryParse(part, out idx)) { if (idx < 0) idx += l.Count; if (idx < 0 || idx >= l.Count) return null; o = l[idx]; continue; }
                return null;
            }
            return o;
        }
        public static string Str(object o, string path, string def = "") { var v = Get(o, path); return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double Num(object o, string path, double def = 0)
        {
            var v = Get(o, path); if (v == null) return def;
            try
            {
                if (v is string) { double d; return double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def; }
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
            catch { return def; }
        }
        public static bool Bool(object o, string path, bool def = false)
        {
            var v = Get(o, path); if (v == null) return def;
            if (v is bool) return (bool)v; if (v is string) return ((string)v).ToLower() == "true";
            try { return Convert.ToDouble(v) != 0; } catch { return def; }
        }
        public static Dictionary<string, object> Obj(object o, string path) { return Get(o, path) as Dictionary<string, object>; }
        public static IList Arr(object o, string path) { return Get(o, path) as IList; }

        /// <summary>Generic value coercion used by settings.</summary>
        public static string AsStr(object v, string def = "") { return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double AsNum(object v, double def = 0) { return Num(v, null, def); }
        public static bool AsBool(object v, bool def = false) { return Bool(v, null, def); }

        static string Pretty(string json)
        {
            var sb = new System.Text.StringBuilder(); int ind = 0; bool q = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\' || (i > 1 && json[i - 2] == '\\'))) q = !q;
                if (q) { sb.Append(c); continue; }
                switch (c)
                {
                    case '{': case '[': sb.Append(c).Append('\n').Append(new string(' ', ++ind * 2)); break;
                    case '}': case ']': sb.Append('\n').Append(new string(' ', --ind * 2)).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(new string(' ', ind * 2)); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
