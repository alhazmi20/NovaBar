# NovaBar widget parity checklist

Generated 2026-11-22 09:24 by `NovaBar.exe --checklist` (reference list: built-in, 109 widgets).

**109 / 109 built** — ✅ 73 live · 🟡 36 needs account / sample data · ❌ 0 missing · ➕ 1 extra

Legend: ✅ Built (live data) · 🟡 Built (needs an account or shows sample data) · ❌ Missing

## AI (11/11)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | AI Limits | Two of your AI plans on one tile (Claude, ChatGPT, Gemini, Grok, Cursor and more), with when each limit resets. | `ailimits` | Live |
| ✅ | Claude Usage | Your Claude session limit and when it resets. | `claude` | Live |
| ✅ | Claude Plan Usage | Session and weekly Claude limits, plus your most-used model. | `claude-plan` | Live |
| ✅ | ChatGPT Usage | Your ChatGPT plan's Codex limits: the 5-hour and weekly windows. | `chatgpt` | Live |
| ✅ | Gemini Usage | Gemini CLI quota for Pro and Flash. | `gemini` | Live |
| 🟡 | Grok Usage | SuperGrok credit use, from your own sign-in. | `grok` | Sample |
| 🟡 | DeepSeek Balance | Your DeepSeek balance, with today's and this week's spend. | `deepseek` | Needs account (deepseek) |
| 🟡 | GLM Usage | Z.ai / Zhipu GLM Coding Plan limits. | `glm` | Needs account (glm) |
| ✅ | OpenCode Usage | OpenCode usage for the current session, from its local history. | `opencode` | Live |
| ✅ | OpenCode Plan Usage | OpenCode tokens this session and this week, and your top model. | `opencode-plan` | Live |
| 🟡 | Cursor Usage | Cursor spend and tokens. | `cursor` | Needs account (cursor) |

## Productivity (10/10)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Day Timeline | Your whole day on one line: hours by kind of app, focus rounds, meetings, sunrise and sunset. | `daytimeline` | Live |
| ✅ | Screen Time | Today's time in front of the PC, hour by hour, and every app's, by day and by week. | `screentime` | Live |
| ✅ | Pomodoro | Focus rounds and breaks you start and skip from the bar. | `pomodoro` | Live |
| ✅ | Timer | A countdown that starts in one click. | `timer` | Live |
| ✅ | Stopwatch | Start, pause and lap from the bar; the card keeps every lap. | `stopwatch` | Live |
| ✅ | Todos | Your next open task with a progress ring; tick it off from the dock. | `todos` | Live |
| ✅ | Notes | Your latest note, one click from editing. | `notes` | Live |
| 🟡 | Calendar | Your next event and how soon it starts. Google, Outlook and iCloud events sit next to renewals and payouts. | `calendar` | Needs account (calendar) |
| ✅ | WhatsApp | Unread WhatsApp messages and who wrote last, read from Windows' own notifications. | `whatsapp` | Live |
| ✅ | Currency | Convert between currencies at current rates. | `currency` | Live |

## Finance (15/15)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| 🟡 | Total Revenue | Every connected store's last 30 days added up, with a monthly goal and its pace. | `totalrevenue` | Needs account |
| 🟡 | Polar | Net revenue over the last 30 days from Polar. | `polar` | Needs account (polar) |
| 🟡 | Polar Chart | Your Polar revenue as a 14-day line. | `polar-chart` | Needs account (polar) |
| 🟡 | Stripe | Net revenue over the last 30 days from Stripe. | `stripe` | Needs account (stripe) |
| 🟡 | Stripe Chart | Your Stripe revenue as a line. | `stripe-chart` | Needs account (stripe) |
| 🟡 | Dodo Payments | Net revenue from Dodo Payments. | `dodo` | Needs account (dodo) |
| 🟡 | Dodo Chart | Your Dodo Payments revenue as a line. | `dodo-chart` | Needs account (dodo) |
| 🟡 | Shopify | Your store's net sales over the last 30 days. | `shopify` | Needs account (shopify) |
| 🟡 | Shopify Chart | This week's Shopify sales and how they moved, over 14 days. | `shopify-chart` | Needs account (shopify) |
| 🟡 | Lemon Squeezy | Revenue after tax and refunds over 30 days. | `lemonsqueezy` | Needs account (lemonsqueezy) |
| 🟡 | Lemon Squeezy Chart | This week's Lemon Squeezy revenue over a 14-day line. | `lemonsqueezy-chart` | Needs account (lemonsqueezy) |
| 🟡 | Paddle | What you earned from Paddle transactions over 30 days. | `paddle` | Needs account (paddle) |
| 🟡 | Paddle Chart | This week's Paddle revenue over a 14-day line. | `paddle-chart` | Needs account (paddle) |
| 🟡 | Gumroad | What you kept from Gumroad sales, after the fee. | `gumroad` | Needs account (gumroad) |
| 🟡 | Gumroad Chart | This week's Gumroad sales over a 14-day line. | `gumroad-chart` | Needs account (gumroad) |

## System (23/23)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Top CPU | The three apps using the most CPU right now; a click opens Task Manager. | `topcpu` | Live |
| ✅ | Top RAM | The three apps holding the most memory, added up as Task Manager does. | `topram` | Live |
| ✅ | Tray | Your tray icons in a grid, one click away; right-click works as in the taskbar. | `tray` | Live |
| ✅ | Language | The current keyboard layout; click to switch. | `language` | Live |
| ✅ | Temperature | CPU temperature as a gauge, with CPU and memory load beside it. | `temperature` | Live |
| ✅ | CPU | Processor load as a ring. | `cpu` | Live |
| ✅ | RAM | Memory in use as a ring. | `ram` | Live |
| ✅ | Network | Download and upload speed with a live chart. | `network` | Live |
| ✅ | Battery | Charge level as a filling gauge, with the charging state. | `battery` | Live |
| ✅ | Battery Ring | The charge as a ring round the number; a spark runs round it while plugged in. | `battery-ring` | Live |
| ✅ | Battery Core | An energy core with twelve cells lit to the charge. | `battery-core` | Live |
| ✅ | Battery Dots | A dot-matrix battery: the number and the charge in dots. | `battery-dots` | Live |
| ✅ | Battery Gauge | A car's fuel gauge, E to F, for your battery. | `battery-gauge` | Live |
| ✅ | Quick Toggles | Wi-Fi, Bluetooth, mobile hotspot and airplane mode. | `quicktoggles` | Live |
| ✅ | Brightness | A slider for the display's brightness. | `brightness` | Live |
| ✅ | Theme Toggle | Switch Windows between light and dark mode. | `themetoggle` | Live |
| ✅ | Do Not Disturb | Silence Windows notifications in one click. | `dnd` | Live |
| ✅ | Notifications | Open the Notification Center from the dock. | `notifcenter` | Live |
| ✅ | Show Desktop | Hide every window, and bring them back. | `showdesktop` | Live |
| ✅ | Keyboard | The on-screen keyboard, one click away. | `keyboard` | Live |
| ✅ | Month Progress | How much of the month is behind you. | `monthprogress` | Live |
| ✅ | Day Progress | How much of today is behind you. | `dayprogress` | Live |
| ✅ | Year Progress | The year as a grid of dots, filling up. | `yearprogress` | Live |

## Media & Audio (6/6)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Now Playing | Cover art, title and full controls for the current track. | `nowplaying` | Live |
| ✅ | Audio Output | Volume for your current speakers or headphones. | `audio` | Live |
| ✅ | Microphone | A call's two buttons for every app at once: mute your microphone, and mute the sound. | `mic` | Live |
| ✅ | Now Playing (compact) | Title, artist and play/pause in a smaller card. | `nowplaying-compact` | Live |
| ✅ | Now Playing Mini | One round play button with the track's progress. | `nowplaying-mini` | Live |
| ✅ | Now Playing Bar | The track with a progress bar and skip. | `nowplaying-bar` | Live |

## Analytics (4/4)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| 🟡 | Search Console | Clicks from Google Search over the last 28 days and how they changed, from your own sign-in. | `searchconsole` | Needs account (searchconsole) |
| 🟡 | DataFast Chart | Your site's daily visitors over 14 days, with the week's total and its change. | `datafast-chart` | Needs account (datafast) |
| 🟡 | Plausible | Visitors this week from Plausible, with the trend. | `plausible` | Needs account (plausible) |
| 🟡 | Dub | Clicks on your Dub links this week. | `dub` | Needs account (dub) |

## Developer (3/3)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| 🟡 | Resend | Emails sent this week and how many were delivered. | `resend` | Needs account (resend) |
| 🟡 | Linear | Issues assigned to you and what's in progress. | `linear` | Needs account (linear) |
| 🟡 | Supabase | API requests on your project, and its health. | `supabase` | Needs account (supabase) |

## Online & Mail (6/6)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Wallpaper Gallery | Browse and set new wallpapers. | `wallpaper` | Live |
| 🟡 | Gmail | Unread mail in your inbox. | `gmail` | Needs account (gmail) |
| 🟡 | GitHub Activity | Your contribution grid and streak. | `github-activity` | Needs account (github) |
| 🟡 | GitHub | Unread GitHub notifications. | `github` | Needs account (github) |
| 🟡 | GitHub Overview | Notifications, open pull requests and stars. | `github-overview` | Needs account (github) |
| 🟡 | LeetCode Stats | Problems solved and your streak. | `leetcode` | Needs account (leetcode) |

## Social (4/4)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| 🟡 | X Profile | Followers on your X account. | `xprofile` | Needs account (x) |
| 🟡 | TikTok | Followers on your TikTok profile. | `tiktok` | Needs account (tiktok) |
| 🟡 | YouTube | Subscribers on your YouTube channel, via your own API key. | `youtube` | Needs account (youtube) |
| 🟡 | Instagram | Followers on your Instagram account, with an official token. | `instagram` | Needs account (instagram) |

## Apps & Files (7/7)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Apps | Start, your pinned apps and every open window. | `apps` | Live |
| ✅ | Recently Opened | The apps and files you used last. | `recent` | Live |
| ✅ | Recycle Bin | What the bin holds, and a click to empty it. | `recyclebin` | Live |
| ✅ | Search | Search the web, or type a folder path to open it. | `search` | Live |
| ✅ | Commands | Type a word to launch an app or an action. | `commands` | Live |
| ✅ | Storage | Free space on your drive, with a usage bar. | `storage` | Live |
| ✅ | Screenshot Capture | Capture the screen, a window or a region. | `screenshot` | Live |

## Weather (6/6)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Weather | Temperature now, with today's high and low. | `weather` | Live |
| ✅ | Weather Details | Wind, rain and the day's range. | `weatherdetails` | Live |
| ✅ | Today & Forecast | Today next to the next three days. | `forecast` | Live |
| ✅ | Weather Status | The conditions in words, with your city. | `weatherstatus` | Live |
| ✅ | Hourly | The next six hours. | `hourly` | Live |
| ✅ | Sunrise & Sunset | Where the sun is between sunrise and sunset. | `sun` | Live |

## Stocks (3/3)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Stock Chart | One ticker with its intraday line. | `stock` | Live |
| ✅ | Stock Watchlist | Three tickers at a glance. | `watchlist` | Live |
| ✅ | Crypto | A coin's price, its change over 24 hours and the day's line, from Binance. | `crypto` | Live |

## Clocks (11/11)

| Status | Reference widget | Description | NovaBar widget id | Data |
|---|---|---|---|---|
| ✅ | Clock | Large, readable time with its seconds and the date, in 12- or 24-hour format. | `clock` | Live |
| ✅ | Flip Clock | Split-flap digits that turn over every minute. | `flipclock` | Live |
| ✅ | World Clocks | Three small analog faces for the cities you work with. | `worldclocks` | Live |
| ✅ | 7 Days | This week as a strip of days, with today's temperature. | `sevendays` | Live |
| ✅ | World Time | One more city in big digits, with its offset. | `worldtime` | Live |
| ✅ | Today | Weekday, date, time and weather in a single tile. | `today` | Live |
| ✅ | Date | The full date, set large. | `date` | Live |
| ✅ | Day Number | Today's date as a calendar page. | `daynumber` | Live |
| ✅ | Analog Minimal | Hands and hour ticks on a dark face, nothing else. | `analog` | Live |
| ✅ | Segment Clock | The time as a seven-segment display, under the weekday and date. | `segmentclock` | Live |
| ✅ | Clock & Weather | Time, date and temperature as three colour blocks in one square tile. | `clockweather` | Live |

## Extra widgets (not in the reference list)

| Widget id | Name | Category | RefName | Data |
|---|---|---|---|---|
| `notifications` | Notifications | Hidden | Notifications (alias) | Live |

