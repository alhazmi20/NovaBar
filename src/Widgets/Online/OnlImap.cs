// Tiny IMAP4rev1 client over SslStream — just enough for unread counts + recent unread headers.
// Used by the Gmail widget with an app password (no OAuth, no extra dependencies).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace NovaBar
{
    public class OnlImap : IDisposable
    {
        TcpClient tcp; SslStream ssl;
        readonly byte[] buf = new byte[16384]; int bl, bp; int tag;
        public const char LitOpen = '\u0001', LitClose = '\u0002';

        public OnlImap(string host, int port)
        {
            tcp = new TcpClient();
            var ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(10000)) { try { tcp.Close(); } catch { } throw new OnlErr("Couldn't reach " + host); }
            try { tcp.EndConnect(ar); } catch { throw new OnlErr("Offline"); }
            tcp.ReceiveTimeout = 15000; tcp.SendTimeout = 15000;
            ssl = new SslStream(tcp.GetStream(), false);
            ssl.ReadTimeout = 15000; ssl.WriteTimeout = 15000;
            ssl.AuthenticateAsClient(host);
            var greet = ReadLine();
            if (!greet.StartsWith("* OK")) throw new OnlErr("Mail server refused the connection");
        }

        int ReadByte()
        {
            if (bp >= bl) { bl = ssl.Read(buf, 0, buf.Length); bp = 0; if (bl <= 0) throw new IOException("closed"); }
            return buf[bp++];
        }
        string ReadLine()
        {
            var ms = new MemoryStream();
            while (true)
            {
                int b = ReadByte();
                if (b == '\n') break;
                if (b != '\r') ms.WriteByte((byte)b);
                if (ms.Length > 1 << 20) throw new IOException("line too long");
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        string ReadN(int n)
        {
            var data = new byte[n];
            for (int i = 0; i < n; i++) data[i] = (byte)ReadByte();
            return Encoding.UTF8.GetString(data);
        }
        /// <summary>One logical response line; literals {n} are inlined between LitOpen/LitClose markers.</summary>
        string ReadResponse()
        {
            var sb = new StringBuilder();
            while (true)
            {
                var l = ReadLine();
                var m = Regex.Match(l, "\\{(\\d+)\\}$");
                if (!m.Success) { sb.Append(l); return sb.ToString(); }
                sb.Append(l.Substring(0, m.Index)).Append(LitOpen).Append(ReadN(int.Parse(m.Groups[1].Value))).Append(LitClose);
            }
        }

        /// <summary>Sends a command; returns untagged lines. Throws OnlErr on NO/BAD.</summary>
        public List<string> Cmd(string command, string failMessage = null)
        {
            var t = "N" + (++tag);
            var bytes = Encoding.UTF8.GetBytes(t + " " + command + "\r\n");
            ssl.Write(bytes, 0, bytes.Length); ssl.Flush();
            var lines = new List<string>();
            while (true)
            {
                var line = ReadResponse();
                if (line.StartsWith(t + " "))
                {
                    var st = line.Substring(t.Length + 1);
                    if (!st.StartsWith("OK")) throw new OnlErr(failMessage ?? ("Mail server: " + st));
                    return lines;
                }
                lines.Add(line);
            }
        }

        public static string Quote(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        public void Dispose()
        {
            try { if (ssl != null) { var b = Encoding.ASCII.GetBytes("Z LOGOUT\r\n"); ssl.Write(b, 0, b.Length); } } catch { }
            try { if (ssl != null) ssl.Dispose(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }

        // ---------------------------------------------------------------- header helpers
        /// <summary>RFC 2047 encoded-word decoding (=?charset?B|Q?...?=).</summary>
        public static string DecodeWords(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.Contains("=?")) return s;
            s = Regex.Replace(s, "\\?=\\s+=\\?", "?==?");
            return Regex.Replace(s, "=\\?([^?]+)\\?([BbQq])\\?([^?]*)\\?=", m =>
            {
                try
                {
                    Encoding enc; try { enc = Encoding.GetEncoding(m.Groups[1].Value.Split('*')[0]); } catch { enc = Encoding.UTF8; }
                    byte[] bytes;
                    if (m.Groups[2].Value.ToUpperInvariant() == "B") bytes = Convert.FromBase64String(m.Groups[3].Value);
                    else
                    {
                        var q = m.Groups[3].Value.Replace('_', ' '); var ms = new MemoryStream();
                        for (int i = 0; i < q.Length; i++)
                        {
                            if (q[i] == '=' && i + 2 < q.Length) { ms.WriteByte(byte.Parse(q.Substring(i + 1, 2), NumberStyles.HexNumber)); i += 2; }
                            else ms.WriteByte((byte)q[i]);
                        }
                        bytes = ms.ToArray();
                    }
                    return enc.GetString(bytes);
                }
                catch { return m.Value; }
            });
        }

        public static Dictionary<string, string> ParseHeaders(string block)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            block = Regex.Replace(block ?? "", "\r?\n[ \t]+", " ");
            foreach (var line in block.Split('\n'))
            {
                int i = line.IndexOf(':'); if (i <= 0) continue;
                var k = line.Substring(0, i).Trim(); var v = line.Substring(i + 1).Trim().TrimEnd('\r');
                if (!d.ContainsKey(k)) d[k] = DecodeWords(v);
            }
            return d;
        }

        public static string DisplayName(string from)
        {
            if (string.IsNullOrEmpty(from)) return "";
            var m = Regex.Match(from, "^\\s*\"?([^\"<]*?)\"?\\s*<([^>]+)>");
            if (m.Success) return m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value.Trim() : m.Groups[2].Value;
            return from.Trim();
        }

        public static DateTime ParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            s = Regex.Replace(s, "\\([^)]*\\)", "").Trim();
            s = Regex.Replace(s, "^[A-Za-z]{3},\\s*", "");
            s = Regex.Replace(s, "\\s([+-])(\\d{2})(\\d{2})$", " $1$2:$3");
            s = Regex.Replace(s, "\\s(GMT|UT|UTC)$", " +00:00");
            DateTimeOffset o;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out o)) return o.UtcDateTime;
            return DateTime.MinValue;
        }
    }
}
