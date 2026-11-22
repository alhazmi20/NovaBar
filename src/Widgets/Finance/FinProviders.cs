// Concrete data providers. Each uses documented, read-only endpoints and returns daily buckets.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace NovaBar
{
    public static class FinProviders
    {
        static readonly Dictionary<string, FinProvider> map = new Dictionary<string, FinProvider>();
        static readonly List<FinProvider> revenue = new List<FinProvider>();
        static FinProviders()
        {
            FinProvider[] rev = { new FinStripe(), new FinPolar(), new FinShopify(), new FinLemon(), new FinPaddle(), new FinGumroad(), new FinDodo() };
            foreach (var p in rev) { map[p.Id] = p; revenue.Add(p); }
            FinProvider[] an = { new FinSearchConsole(), new FinDataFast(), new FinPlausible(), new FinDub() };
            foreach (var p in an) map[p.Id] = p;

            Accounts.Define("stripe", "Stripe", "Create a restricted key with read access to Balance (balance_transactions). Net revenue = charges minus fees and refunds.", "https://dashboard.stripe.com/apikeys",
                Accounts.F("key", "Restricted key", true, "rk_live_..."));
            Accounts.Define("polar", "Polar", "Create an Organization Access Token (Settings → Developers) with the metrics:read and orders:read scopes.", "https://polar.sh/dashboard",
                Accounts.F("token", "Organization access token", true, "polar_oat_..."));
            Accounts.Define("shopify", "Shopify", "Create a custom app (Settings → Apps → Develop apps) with the read_orders Admin API scope, install it, then paste the Admin API access token and your shop domain.", "https://admin.shopify.com/settings/apps/development",
                Accounts.F("shop", "Shop domain", false, "your-store.myshopify.com"), Accounts.F("token", "Admin API access token", true, "shpat_..."));
            Accounts.Define("lemonsqueezy", "Lemon Squeezy", "Create an API key under Settings → API. Orders from all your stores are added up (after tax and refunds).", "https://app.lemonsqueezy.com/settings/api",
                Accounts.F("key", "API key"));
            Accounts.Define("paddle", "Paddle", "Paddle Billing: Developer tools → Authentication → create an API key with Transactions: read. Sandbox keys (pdl_sdbx_) use the sandbox API.", "https://vendors.paddle.com/authentication-v2",
                Accounts.F("key", "API key", true, "pdl_live_apikey_..."));
            Accounts.Define("gumroad", "Gumroad", "Settings → Advanced → Applications: create an application, then “Generate access token”.", "https://app.gumroad.com/settings/advanced",
                Accounts.F("token", "Access token"));
            Accounts.Define("dodo", "Dodo Payments", "Developer → API Keys: create a key (read access is enough). Test keys (dodo_test_) use the test environment.", "https://app.dodopayments.com/developer/api-keys",
                Accounts.F("key", "API key"));
            Accounts.Define("searchconsole", "Google Search Console", "Create an OAuth client (Desktop app) in Google Cloud with the Search Console API enabled, then get a refresh token for scope https://www.googleapis.com/auth/webmasters.readonly (e.g. via the OAuth Playground using your own client). Site is “sc-domain:example.com” or “https://example.com/”.", "https://console.cloud.google.com/apis/credentials",
                Accounts.F("client_id", "OAuth client ID", false), Accounts.F("client_secret", "OAuth client secret"), Accounts.F("refresh_token", "Refresh token"), Accounts.F("site", "Property", false, "sc-domain:example.com"));
            Accounts.Define("datafast", "DataFast", "Website settings → API: create a website API key (df_...).", "https://datafa.st/dashboard",
                Accounts.F("key", "Website API key", true, "df_..."));
            Accounts.Define("plausible", "Plausible", "Account settings → API Keys: create a Stats API key. Use https://plausible.io as the base URL unless you self-host.", "https://plausible.io/settings/api-keys",
                Accounts.F("base", "Base URL", false, "https://plausible.io"), Accounts.F("site", "Site domain", false, "example.com"), Accounts.F("key", "Stats API key"));
            Accounts.Define("dub", "Dub", "Workspace settings → API Keys: create a key with analytics read access (analytics needs a Pro plan).", "https://app.dub.co/settings/tokens",
                Accounts.F("key", "API key", true, "dub_..."));
        }
        /// <summary>Touching this runs the static ctor (defines all accounts).</summary>
        public static void Init() { }
        public static FinProvider Get(string id) { FinProvider p; return map.TryGetValue(id, out p) ? p : null; }
        public static IEnumerable<FinProvider> All { get { return map.Values; } }
        public static IList<FinProvider> Revenue { get { return revenue; } }
        internal static string Bearer(string tok) { return "Authorization: Bearer " + tok; }
    }

    // ---------------------------------------------------------------- Stripe
    public class FinStripe : FinProvider
    {
        public FinStripe() { Id = "stripe"; Name = "Stripe"; Account = "stripe"; Letter = "S"; Brand = Color.FromArgb(99, 91, 255); Dashboard = "https://dashboard.stripe.com/balance"; SampleBase = 310; Seed = 3; SampleLabels = new[] { "Pro monthly", "Pro yearly", "Team seat", "Starter" }; }
        public override FinSeries Fetch()
        {
            var key = Acc("key"); var b = new FinBuilder(Id, History);
            string after = null;
            for (int page = 0; page < 40; page++)
            {
                var url = "https://api.stripe.com/v1/balance_transactions?limit=100&created%5Bgte%5D=" + FinFmt.Unix(b.Start) + (after != null ? "&starting_after=" + Http.Esc(after) : "");
                var o = FinNet.Json("GET", url, null, FinProviders.Bearer(key));
                var data = J.Arr(o, "data"); if (data == null || data.Count == 0) break;
                foreach (var t in data)
                {
                    after = J.Str(t, "id");
                    var type = J.Str(t, "type");
                    bool sale = type == "charge" || type == "payment";
                    if (!sale && type != "refund" && type != "payment_refund") continue;
                    var cur = J.Str(t, "currency", "usd").ToUpperInvariant(); double div = FinFmt.Minor(cur);
                    var when = FinFmt.FromUnix(J.Num(t, "created"));
                    b.Currency(cur); b.Add(when, J.Num(t, "net") / div, sale ? 1 : 0);
                    if (sale) b.Txn(when, J.Num(t, "amount") / div, J.Str(t, "description", "Payment"));
                }
                if (!J.Bool(o, "has_more")) break;
            }
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Polar
    public class FinPolar : FinProvider
    {
        public FinPolar() { Id = "polar"; Name = "Polar"; Account = "polar"; Letter = "P"; Brand = Color.FromArgb(0, 98, 255); Dashboard = "https://polar.sh/dashboard"; SampleBase = 140; Seed = 5; SampleLabels = new[] { "Sponsor tier", "Pro license", "Course", "Team plan" }; }
        public override FinSeries Fetch()
        {
            var tok = Acc("token"); var b = new FinBuilder(Id, History);
            // Metrics API: daily buckets, currency values in cents. Buckets are UTC days.
            var o = FinNet.Json("GET", "https://api.polar.sh/v1/metrics/?interval=day&start_date=" + FinFmt.Iso(b.Start) + "&end_date=" + FinFmt.Iso(b.End), null, FinProviders.Bearer(tok));
            var periods = J.Arr(o, "periods");
            if (periods == null) throw new FinHttpException("Unexpected response from Polar", 0);
            foreach (var p in periods)
            {
                var net = J.Get(p, "net_revenue"); double cents = net != null ? J.AsNum(net) : J.Num(p, "revenue");
                b.AddDay(J.Str(p, "timestamp"), cents / 100.0, (int)J.Num(p, "orders"));
            }
            try
            {
                var orders = FinNet.Json("GET", "https://api.polar.sh/v1/orders/?limit=5&sorting=-created_at", null, FinProviders.Bearer(tok));
                var items = J.Arr(orders, "items");
                if (items != null) foreach (var it in items)
                    {
                        var cur = J.Str(it, "currency", "usd").ToUpperInvariant();
                        double amt = J.Num(it, "net_amount", J.Num(it, "amount")) / FinFmt.Minor(cur);
                        var label = J.Str(it, "product.name", "Order"); var email = J.Str(it, "customer.email");
                        if (email.Length > 0) label += " · " + email;
                        b.Txn(FinFmt.Local(J.Str(it, "created_at")), amt, label);
                    }
            }
            catch { } // token may lack orders:read; metrics alone are fine
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Shopify
    public class FinShopify : FinProvider
    {
        public FinShopify() { Id = "shopify"; Name = "Shopify"; Account = "shopify"; Letter = "S"; Brand = Color.FromArgb(94, 142, 62); Dashboard = "https://admin.shopify.com"; SampleBase = 230; Seed = 7; SampleLabels = new[] { "#1042", "#1043", "#1044", "#1045" }; }
        public override FinSeries Fetch()
        {
            var shop = (Acc("shop") ?? "").Trim().ToLowerInvariant().Replace("https://", "").Replace("http://", "").TrimEnd('/');
            int slash = shop.IndexOf('/'); if (slash > 0) shop = shop.Substring(0, slash);
            if (shop.IndexOf('.') < 0) shop += ".myshopify.com";
            var tok = Acc("token"); var b = new FinBuilder(Id, History);
            string url = "https://" + shop + "/admin/api/2024-10/orders.json?status=any&limit=250&created_at_min=" + Http.Esc(FinFmt.IsoUtc(b.Start)) +
                "&fields=id,name,created_at,currency,current_subtotal_price,subtotal_price,total_price,cancelled_at,financial_status";
            for (int page = 0; page < 40 && url != null; page++)
            {
                string link; var o = FinNet.Parse(FinNet.Send("GET", url, null, null, out link, "X-Shopify-Access-Token: " + tok));
                var orders = J.Arr(o, "orders"); if (orders == null) break;
                foreach (var it in orders)
                {
                    if (J.Get(it, "cancelled_at") != null || J.Str(it, "financial_status") == "voided") continue;
                    var when = FinFmt.Local(J.Str(it, "created_at"));
                    var sub = J.Get(it, "current_subtotal_price"); double net = sub != null ? J.AsNum(sub) : J.Num(it, "subtotal_price");
                    b.Currency(J.Str(it, "currency")); b.Add(when, net);
                    b.Txn(when, J.Num(it, "total_price"), "Order " + J.Str(it, "name"));
                }
                url = FinNet.NextLink(link);
            }
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Lemon Squeezy
    public class FinLemon : FinProvider
    {
        public FinLemon() { Id = "lemonsqueezy"; Name = "Lemon Squeezy"; Account = "lemonsqueezy"; Letter = "L"; Brand = Color.FromArgb(112, 71, 235); Dashboard = "https://app.lemonsqueezy.com/orders"; SampleBase = 95; Seed = 11; SampleLabels = new[] { "Icon pack", "UI kit", "Template bundle", "Pro license" }; }
        public override FinSeries Fetch()
        {
            var key = Acc("key"); var b = new FinBuilder(Id, History);
            for (int page = 1; page <= 30; page++)
            {
                var o = FinNet.Json("GET", "https://api.lemonsqueezy.com/v1/orders?page%5Bsize%5D=100&page%5Bnumber%5D=" + page, null,
                    FinProviders.Bearer(key), "Accept: application/vnd.api+json");
                var data = J.Arr(o, "data"); if (data == null || data.Count == 0) break;
                bool anyInRange = false;
                foreach (var it in data)
                {
                    var a = J.Obj(it, "attributes"); if (a == null) continue;
                    var when = FinFmt.Local(J.Str(a, "created_at"));
                    if (b.InRange(when)) anyInRange = true; else continue;
                    var st = J.Str(a, "status");
                    if (st != "paid" && st != "refunded" && st != "partial_refund") continue;
                    var cur = J.Str(a, "currency", "USD"); double div = FinFmt.Minor(cur);
                    double net = Math.Max(0, J.Num(a, "total") - J.Num(a, "tax") - J.Num(a, "refunded_amount")) / div;
                    b.Currency(cur); b.Add(when, net);
                    var label = J.Str(a, "first_order_item.product_name", "Order"); var who = J.Str(a, "user_name");
                    if (who.Length > 0) label += " · " + who;
                    b.Txn(when, J.Num(a, "total") / div, label);
                }
                if (!anyInRange) break; // newest first: everything after this page is older
                if (J.Num(o, "meta.page.lastPage", page) <= page) break;
            }
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Paddle Billing
    public class FinPaddle : FinProvider
    {
        public FinPaddle() { Id = "paddle"; Name = "Paddle"; Account = "paddle"; Letter = "P"; Brand = Color.FromArgb(253, 221, 53); Dashboard = "https://vendors.paddle.com/transactions-v2"; SampleBase = 170; Seed = 13; SampleLabels = new[] { "Business plan", "Pro plan", "Add-on seats", "Annual plan" }; }
        public override FinSeries Fetch()
        {
            var key = Acc("key"); var b = new FinBuilder(Id, History);
            string api = key.Contains("_sdbx_") ? "https://sandbox-api.paddle.com" : "https://api.paddle.com";
            string url = api + "/transactions?status=billed,completed&per_page=100&order_by=created_at%5BDESC%5D&created_at%5BGTE%5D=" + Http.Esc(FinFmt.IsoUtc(b.Start));
            for (int page = 0; page < 40 && !string.IsNullOrEmpty(url); page++)
            {
                var o = FinNet.Json("GET", url, null, FinProviders.Bearer(key), "Paddle-Version: 1");
                var data = J.Arr(o, "data"); if (data == null) break;
                foreach (var t in data)
                {
                    var tot = J.Obj(t, "details.totals"); if (tot == null) continue;
                    var cur = J.Str(tot, "currency_code", J.Str(t, "currency_code", "USD")); double div = FinFmt.Minor(cur);
                    var earn = J.Get(tot, "earnings");
                    double net = (earn != null ? J.AsNum(earn) : J.Num(tot, "subtotal") - J.Num(tot, "discount")) / div;
                    var when = FinFmt.Local(J.Str(t, "billed_at", J.Str(t, "created_at")));
                    if (when == DateTime.MinValue) when = FinFmt.Local(J.Str(t, "created_at"));
                    b.Currency(cur); b.Add(when, net);
                    var label = J.Str(t, "items.0.price.description"); if (label.Length == 0) label = J.Str(t, "items.0.price.name", "Transaction");
                    b.Txn(when, J.Num(tot, "grand_total", J.Num(tot, "total")) / div, label);
                }
                url = J.Bool(o, "meta.pagination.has_more") ? J.Str(o, "meta.pagination.next") : null;
            }
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Gumroad
    public class FinGumroad : FinProvider
    {
        public FinGumroad() { Id = "gumroad"; Name = "Gumroad"; Account = "gumroad"; Letter = "G"; Brand = Color.FromArgb(255, 144, 232); Dashboard = "https://app.gumroad.com/sales"; SampleBase = 55; Seed = 17; SampleLabels = new[] { "Notion template", "E-book", "Preset pack", "Video course" }; }
        public override FinSeries Fetch()
        {
            var tok = Acc("token"); var b = new FinBuilder(Id, History);
            string pageKey = null;
            for (int page = 0; page < 40; page++)
            {
                var url = "https://api.gumroad.com/v2/sales?after=" + FinFmt.Iso(b.Start) + "&access_token=" + Http.Esc(tok) + (pageKey != null ? "&page_key=" + Http.Esc(pageKey) : "");
                var o = FinNet.Json("GET", url, null);
                if (!J.Bool(o, "success", true)) throw new FinHttpException("Gumroad: " + J.Str(o, "message", "request failed"), 0);
                var sales = J.Arr(o, "sales"); if (sales == null) break;
                foreach (var s in sales)
                {
                    if (J.Bool(s, "refunded") || J.Bool(s, "chargedback")) continue;
                    var when = FinFmt.Local(J.Str(s, "created_at"));
                    double kept = (J.Num(s, "price") - J.Num(s, "gumroad_fee")) / 100.0; // cents, USD
                    b.Add(when, kept);
                    var label = J.Str(s, "product_name", "Sale"); var email = J.Str(s, "email");
                    if (email.Length > 0) label += " · " + email;
                    b.Txn(when, J.Num(s, "price") / 100.0, label);
                }
                pageKey = J.Str(o, "next_page_key", null);
                if (string.IsNullOrEmpty(pageKey)) break;
            }
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Dodo Payments
    public class FinDodo : FinProvider
    {
        public FinDodo() { Id = "dodo"; Name = "Dodo Payments"; Account = "dodo"; Letter = "D"; Brand = Color.FromArgb(255, 196, 0); Dashboard = "https://app.dodopayments.com/transactions/payments"; SampleBase = 70; Seed = 19; SampleLabels = new[] { "Pro plan", "Credits pack", "License", "Annual plan" }; }
        public override FinSeries Fetch()
        {
            var key = Acc("key"); var b = new FinBuilder(Id, History);
            string api = key.StartsWith("dodo_test", StringComparison.OrdinalIgnoreCase) ? "https://test.dodopayments.com" : "https://live.dodopayments.com";
            for (int page = 0; page < 40; page++)
            {
                var o = FinNet.Json("GET", api + "/payments?status=succeeded&page_size=100&page_number=" + page + "&created_at_gte=" + Http.Esc(FinFmt.IsoUtc(b.Start)), null, FinProviders.Bearer(key));
                var items = J.Arr(o, "items"); if (items == null || items.Count == 0) break;
                foreach (var it in items)
                {
                    var cur = J.Str(it, "currency", "USD"); double div = FinFmt.Minor(cur);
                    var when = FinFmt.Local(J.Str(it, "created_at"));
                    double total = J.Num(it, "total_amount") / div, tax = J.Num(it, "tax") / div;
                    b.Currency(cur); b.Add(when, Math.Max(0, total - tax));
                    var who = J.Str(it, "customer.name"); if (who.Length == 0) who = J.Str(it, "customer.email", "Payment");
                    b.Txn(when, total, who);
                }
                if (items.Count < 100) break;
            }
            return b.Done();
        }
    }

    // ---------------------------------------------------------------- Google Search Console
    public class FinSearchConsole : FinProvider
    {
        static string accessToken; static DateTime accessExpiry; static string accessFor;
        public FinSearchConsole() { Id = "searchconsole"; Name = "Search Console"; Account = "searchconsole"; Letter = "G"; Brand = Color.FromArgb(66, 133, 244); Dashboard = "https://search.google.com/search-console"; Money = false; History = 56; Unit = "clicks"; SampleBase = 420; Seed = 23; }
        string Token()
        {
            string cid = Acc("client_id"), sec = Acc("client_secret"), rt = Acc("refresh_token");
            if (accessToken != null && accessFor == rt && DateTime.UtcNow < accessExpiry) return accessToken;
            string link;
            var body = "client_id=" + Http.Esc(cid) + "&client_secret=" + Http.Esc(sec) + "&refresh_token=" + Http.Esc(rt) + "&grant_type=refresh_token";
            var o = FinNet.Parse(FinNet.Send("POST", "https://oauth2.googleapis.com/token", body, "application/x-www-form-urlencoded", out link));
            var at = J.Str(o, "access_token"); if (at.Length == 0) throw new FinHttpException("Google sign-in failed", 401);
            accessToken = at; accessFor = rt; accessExpiry = DateTime.UtcNow.AddSeconds(J.Num(o, "expires_in", 3600) - 120);
            return at;
        }
        public override FinSeries Fetch()
        {
            var tok = Token(); var site = Acc("site").Trim();
            DateTime end = DateTime.Today, start = end.AddDays(-(History + 6));
            var body = "{\"startDate\":\"" + FinFmt.Iso(start) + "\",\"endDate\":\"" + FinFmt.Iso(end) + "\",\"dimensions\":[\"date\"],\"rowLimit\":1000,\"dataState\":\"all\"}";
            var o = FinNet.Json("POST", "https://www.googleapis.com/webmasters/v3/sites/" + Http.Esc(site) + "/searchAnalytics/query", body, FinProviders.Bearer(tok));
            var rows = J.Arr(o, "rows");
            // Search data lags 1-3 days: end the series on the last day that has data.
            DateTime last = DateTime.MinValue;
            if (rows != null) foreach (var r in rows) { DateTime d; if (DateTime.TryParseExact(J.Str(r, "keys.0"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d > last) last = d; }
            var b = new FinBuilder(Id, History, last == DateTime.MinValue ? end.AddDays(-2) : last);
            if (rows != null) foreach (var r in rows) b.AddDay(J.Str(r, "keys.0"), J.Num(r, "clicks"));
            return b.Done(false);
        }
    }

    // ---------------------------------------------------------------- DataFast
    public class FinDataFast : FinProvider
    {
        public FinDataFast() { Id = "datafast"; Name = "DataFast"; Account = "datafast"; Letter = "D"; Brand = Color.FromArgb(232, 117, 58); Dashboard = "https://datafa.st/dashboard"; Money = false; History = 28; Unit = "visitors"; SampleBase = 640; Seed = 29; }
        public override FinSeries Fetch()
        {
            var b = new FinBuilder(Id, History);
            var o = FinNet.Json("GET", "https://datafa.st/api/v1/analytics/timeseries?fields=visitors&interval=day&startAt=" + FinFmt.Iso(b.Start) + "&endAt=" + FinFmt.Iso(b.End), null, FinProviders.Bearer(Acc("key")));
            var data = J.Arr(o, "data"); if (data == null) throw new FinHttpException("DataFast: " + J.Str(o, "error.message", J.Str(o, "message", "unexpected response")), 0);
            foreach (var r in data) b.AddDay(J.Str(r, "timestamp", J.Str(r, "name")), J.Num(r, "visitors"));
            return b.Done(false);
        }
    }

    // ---------------------------------------------------------------- Plausible
    public class FinPlausible : FinProvider
    {
        public FinPlausible() { Id = "plausible"; Name = "Plausible"; Account = "plausible"; Letter = "P"; Brand = Color.FromArgb(88, 80, 236); Dashboard = "https://plausible.io/sites"; Money = false; History = 28; Unit = "visitors"; SampleBase = 520; Seed = 31; }
        public override FinSeries Fetch()
        {
            var bas = (Acc("base") ?? "https://plausible.io").Trim().TrimEnd('/'); if (!bas.StartsWith("http")) bas = "https://" + bas;
            var site = Acc("site").Trim(); var key = Acc("key"); var b = new FinBuilder(Id, History);
            try
            {
                // Stats API v2
                var body = "{\"site_id\":" + J.Stringify(site) + ",\"metrics\":[\"visitors\"],\"date_range\":[\"" + FinFmt.Iso(b.Start) + "\",\"" + FinFmt.Iso(b.End) + "\"],\"dimensions\":[\"time:day\"]}";
                var o = FinNet.Json("POST", bas + "/api/v2/query", body, FinProviders.Bearer(key));
                var res = J.Arr(o, "results"); if (res == null) throw new FinHttpException("Unexpected response from Plausible", 0);
                foreach (var r in res) b.AddDay(J.Str(r, "dimensions.0"), J.Num(r, "metrics.0"));
            }
            catch (FinHttpException e)
            {
                if (e.Status != 404) throw;
                // Older self-hosted instances: Stats API v1
                var o = FinNet.Json("GET", bas + "/api/v1/stats/timeseries?site_id=" + Http.Esc(site) + "&period=custom&date=" + FinFmt.Iso(b.Start) + "," + FinFmt.Iso(b.End) + "&metrics=visitors", null, FinProviders.Bearer(key));
                var res = J.Arr(o, "results"); if (res == null) throw new FinHttpException("Unexpected response from Plausible", 0);
                foreach (var r in res) b.AddDay(J.Str(r, "date"), J.Num(r, "visitors"));
            }
            return b.Done(false);
        }
    }

    // ---------------------------------------------------------------- Dub
    public class FinDub : FinProvider
    {
        public FinDub() { Id = "dub"; Name = "Dub"; Account = "dub"; Letter = "D"; Brand = Color.FromArgb(20, 20, 22); Dashboard = "https://app.dub.co"; Money = false; History = 28; Unit = "clicks"; SampleBase = 180; Seed = 37; }
        public override FinSeries Fetch()
        {
            var b = new FinBuilder(Id, History);
            var o = FinNet.Json("GET", "https://api.dub.co/analytics?event=clicks&groupBy=timeseries&start=" + FinFmt.Iso(b.Start) + "&end=" + FinFmt.Iso(b.End.AddDays(1)), null, FinProviders.Bearer(Acc("key")));
            var arr = o as System.Collections.IList; if (arr == null) throw new FinHttpException("Dub: " + J.Str(o, "error.message", "unexpected response"), 0);
            foreach (var r in arr) b.AddDay(J.Str(r, "start"), J.Num(r, "clicks"));
            return b.Done(false);
        }
    }
}
