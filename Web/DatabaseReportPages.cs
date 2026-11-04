using System.Globalization;
using System.Net;
using System.Text;
using Prediction.Entities;
using Prediction.Services;

namespace Prediction.Web;

public enum UiLanguage { English, Macedonian }

public static class DatabaseReportPages
{
    public static string Encode(object? value) =>
        WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    public static UiLanguage ParseLanguage(string? value) =>
        string.Equals(value, "mk", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Macedonian : UiLanguage.English;

    public static string Text(UiLanguage language, string english) =>
        language == UiLanguage.English ? english : english switch
        {
            "Football analytics" => "Фудбалска аналитика",
            "SIMULATION ONLY" => "САМО СИМУЛАЦИЈА",
            "1 unit is staked on the whole ticket; these are not actual payments. Source probabilities are not calibrated ticket probabilities. Pages display stored records only and do not collect or generate tickets." =>
                "Се вложува 1 единица на целиот тикет; ова не се вистински уплати. Веројатностите од изворите не се калибрирани веројатности за тикет. Страниците прикажуваат само зачувани записи и не собираат или генерираат тикети.",
            "Overview" => "Почетна",
            "Predictions" => "Прогнози",
            "Tickets" => "Тикети",
            "Performance" => "Успешност",
            "Leagues" => "Лиги",
            "Main navigation" => "Главна навигација",
            "Match intelligence" => "Натпреварувачка аналитика",
            "Explore analytics" => "Истражи ја статистиката",
            "Matchday, at a glance" => "Натпреварите на едно место",
            "Matchday dashboard" => "Табла на натпреварите",
            "Make every fixture" => "Искористи го секој натпревар",
            "count." => "паметно.",
            "Explore stored predictions, compare ticket simulations, and track results in one place." =>
                "Прегледај ги зачуваните прогнози, спореди симулации на тикети и следи ги резултатите на едно место.",
            "Choose a match date" => "Избери датум на натпревар",
            "View predictions" => "Прикажи прогнози",
            "Collect predictions" => "Собери прогнози",
            "Ticket simulations" => "Симулации на тикети",
            "Browse saved ticket variants, frozen picks, and settlement status." =>
                "Прегледај зачувани варијанти на тикети, фиксни избори и статус на порамнување.",
            "Performance & ROI" => "Успешност и ROI",
            "Review simulated returns across dates and ticket strategies." =>
                "Прегледај симулирани добивки според датум и стратегија на тикети.",
            "League insights" => "Статистика по лиги",
            "Compare historical consensus accuracy by league and market." =>
                "Спореди ја историската точност на консензусот по лига и пазар.",
            "Daily automation targets 08:00 Europe/Skopje; GitHub scheduling may be delayed." =>
                "Дневната автоматизација е планирана за 08:00 часот (Europe/Skopje); GitHub може да ја започне со задоцнување.",
            "Stored predictions and tickets are available without Excel files." =>
                "Зачуваните прогнози и тикети се достапни без Excel-датотеки.",
            "View run history and errors →" => "Прегледај ја историјата и грешките →",
            "Analysis complete" => "Анализата е завршена",
            "Prediction date:" => "Датум на прогноза:",
            "← Back to dashboard" => "← Назад на почетната",
            "Something went wrong" => "Настана грешка",
            "Analysis could not be completed" => "Анализата не можеше да се заврши",
            "Analysis error" => "Грешка при анализа",
            "Prediction results" => "Резултати од прогнозите",
            "Stored predictions" => "Зачувани прогнози",
            "Stored tickets" => "Зачувани тикети",
            "Ticket performance" => "Успешност на тикетите",
            "League statistics" => "Статистика по лиги",
            "Date" => "Датум",
            "Show stored predictions" => "Прикажи зачувани прогнози",
            "stored fixtures for" => "зачувани натпревари за",
            "Latest snapshot per source; capture times are UTC." =>
                "Најнова снимка од секој извор; времињата на зачувување се UTC.",
            "No stored predictions for this date." => "Нема зачувани прогнози за овој датум.",
            "Fixture / league" => "Натпревар / лига",
            "Source" => "Извор",
            "Consensus" => "Консензус",
            "Source details" => "Детали по извор",
            "Score" => "Резултат",
            "Goals" => "Голови",
            "Source Prob." => "Веројатност од извор",
            "Source average" => "Просек од извори",
            "Agreement" => "Согласување",
            "Sort by" => "Сортирај по",
            "Direction" => "Насока",
            "Descending" => "Опаѓачки",
            "Ascending" => "Растечки",
            "with probability" => "со процент",
            "Agreement is the share of voting sources, not a probability of winning. Source averages use only agreeing sources with valid market-specific percentages; missing percentages are excluded, never replaced with 0% or 50%. BTTS/goals source percentages are not currently stored, so their averages are n/a." =>
                "Согласувањето е удел на извори со ист тип, не веројатност за добивка. Просекот ги користи само согласните извори со валиден процент за тој пазар; непознатите не се заменуваат со 0% или 50%. Изворни проценти за BTTS/голови моментално не се зачувуваат, па нивниот просек е н/д.",
            "Captured UTC" => "Зачувано (UTC)",
            "Final / half-time" => "Крај / полувреме",
            "No stored snapshots." => "Нема зачувани снимки.",
            "Ticket" => "Тикет",
            "Mode" => "Режим",
            "Picks" => "Избори",
            "Status" => "Статус",
            "Odds" => "Коефициент",
            "Stake" => "Влог",
            "Profit" => "Добивка",
            "Created UTC" => "Креирано (UTC)",
            "Pending" => "Во тек",
            "Won" => "Добитен",
            "Lost" => "Губитен",
            "Won count" => "Добитни",
            "Lost count" => "Губитни",
            "Frozen selections and current results" => "Фиксни избори и тековни резултати",
            "Market / pick" => "Пазар / избор",
            "FT / HT" => "Крај / полувреме",
            "Details" => "Детали",
            "Evidence / pending reason" => "Извор / причина за чекање",
            "Historical accuracy:" => "Историска точност:",
            "Wilson lower bound:" => "Долна Wilson граница:",
            "sample:" => "примерок:",
            "Kickoff UTC:" => "Почеток (UTC):",
            "From" => "Од",
            "To" => "До",
            "Show stored records" => "Прикажи зачувани записи",
            "Page" => "Страница",
            "records" => "записи",
            "← Previous" => "← Претходна",
            "Next →" => "Следна →",
            "All modes" => "Сите режими",
            "No saved tickets in this range." => "Нема зачувани тикети во овој период.",
            "Current stored results applied to frozen selections. Pending stakes are excluded from settled ROI. Overlapping tickets are not independent evidence." =>
                "Тековните зачувани резултати се применети на фиксните избори. Влоговите на тикети што се во тек не се вклучени во пресметаниот ROI. Тикетите што се преклопуваат не се независен доказ.",
            "Settled stake" => "Порамнет влог",
            "Pending stake" => "Влог во тек",
            "ROI" => "ROI",
            "ALL" => "СИТЕ",
            "Equal-vote consensus; only snapshots BEFORE 00:00 UTC on match date. Missing league labels remain unknown. Accuracy is not ROI. Samples under 30 are not reliable." =>
                "Консензус со еднаков глас; само снимки ПРЕД 00:00 UTC на денот на натпреварот. Непознатите лиги остануваат означени како непознати. Точноста не е ROI. Примероци под 30 не се сигурни.",
            "League" => "Лига",
            "Market" => "Пазар",
            "Correct" => "Точни",
            "Graded" => "Оценети",
            "Accuracy" => "Точност",
            "Wilson lower bound" => "Долна Wilson граница",
            "Sample" => "Примерок",
            "Small sample" => "Мал примерок",
            "30+ observations" => "30+ набљудувања",
            "No graded pre-date consensus picks in this range." => "Нема оценети консензус-избори пред датумот во овој период.",
            "missing final result (unfinished, unavailable or unmatched; not inferred)" =>
                "недостасува конечен резултат (натпреварот не е завршен, резултатот не е достапен или натпреварот не е усогласен; не е претпоставен)",
            "missing required half-time score" => "недостасува потребниот резултат на полувреме",
            "n/a" => "н/д",
            _ => english
        };

    public static string Page(string title, string body, bool showNotice = true, UiLanguage language = UiLanguage.English)
    {
        string langCode = LanguageCode(language);
        return $$$"""
        <!doctype html>
        <html lang="{{{langCode}}}"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="theme-color" content="#080d18">
        <title>{{{Encode(Text(language, title))}}} - Football AI</title>
        <style>
        :root{color-scheme:dark;--bg:#080d18;--surface:#101827;--surface-raised:#151f30;--line:#243248;--text:#edf3fb;--muted:#93a2b8;--accent:#b9f36a;--blue:#8eb7ff;--red:#ff8f91;--amber:#f7ce76;--radius:18px}
        *{box-sizing:border-box}
        body{margin:0;min-height:100vh;background:radial-gradient(ellipse at 82% -10%,#1e3448 0,transparent 36%),radial-gradient(ellipse at 0% 40%,#142333 0,transparent 36%),var(--bg);color:var(--text);font:15px/1.6 Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
        a{color:var(--accent);text-decoration:none;transition:color .18s ease,background .18s ease,border-color .18s ease}
        a:hover{color:#d7ff9f}
        main{width:min(1440px,100%);margin:0 auto;padding:28px clamp(18px,4vw,56px) 56px}
        .topbar{display:flex;align-items:center;justify-content:space-between;gap:24px;padding:4px 0 24px;border-bottom:1px solid var(--line);margin-bottom:38px}
        .brand{display:inline-flex;align-items:center;gap:11px;color:var(--text);font-size:16px;font-weight:750;letter-spacing:-.03em}
        .brand-mark{display:grid;place-items:center;width:36px;height:36px;border-radius:12px;background:var(--accent);color:#15200a;font-size:19px}
        .brand small{display:block;color:var(--muted);font-size:10px;font-weight:650;letter-spacing:.13em;text-transform:uppercase;line-height:1.2}
        nav{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:6px}
        nav a{padding:8px 12px;border:1px solid transparent;border-radius:999px;color:#b1bfd1;font-size:13px;font-weight:600}
        nav a:hover{border-color:var(--line);background:#ffffff08;color:var(--text)}
        .language-switch{display:inline-flex;align-items:center;gap:2px;margin-left:8px;padding:3px;border:1px solid var(--line);border-radius:999px}
        .language-switch a{padding:5px 8px;font-size:11px}
        h1,h2,h3,p{margin-top:0}
        h1{font-size:clamp(28px,4vw,42px);line-height:1.12;letter-spacing:-.045em;margin-bottom:10px}
        h2{font-size:21px;letter-spacing:-.025em}
        h3{font-size:16px;letter-spacing:-.015em}
        .page-heading{margin-bottom:24px}
        .eyebrow{color:var(--accent);font-size:11px;font-weight:750;letter-spacing:.16em;text-transform:uppercase}
        .muted,small{color:var(--muted)}
        .notice{display:flex;gap:13px;align-items:flex-start;padding:15px 18px;margin:0 0 24px;border:1px solid #304038;border-radius:14px;background:#17231b;color:#c5d0bf;font-size:13px}
        .notice strong{color:var(--accent);white-space:nowrap}
        .panel,.card{border:1px solid var(--line);border-radius:var(--radius);background:linear-gradient(145deg,#141f30f2,#0e1725f2);box-shadow:0 18px 60px #00000020}
        .result-panel{padding:clamp(20px,4vw,36px)}
        form{display:flex;flex-wrap:wrap;align-items:end;gap:12px;padding:18px;margin:0 0 22px;border:1px solid var(--line);border-radius:16px;background:#101827c9}
        label{display:grid;gap:6px;color:#adbacb;font-size:12px;font-weight:650}
        input,select,button{font:inherit}
        input,select{min-height:44px;padding:9px 12px;border:1px solid #34445b;border-radius:11px;background:#0b1220;color:var(--text);accent-color:var(--accent)}
        input:focus,select:focus{outline:2px solid #b9f36a88;outline-offset:1px;border-color:var(--accent)}
        button,.button{display:inline-flex;align-items:center;justify-content:center;gap:9px;min-height:44px;padding:10px 16px;border:1px solid var(--accent);border-radius:11px;background:var(--accent);color:#14200a;font-size:13px;font-weight:800;cursor:pointer;transition:transform .18s ease,filter .18s ease}
        button:hover,.button:hover{transform:translateY(-1px);filter:brightness(1.05);color:#14200a}
        .button-secondary{border-color:#34445b;background:#172235;color:var(--text)}
        .button-secondary:hover{background:#1d2b40;color:#fff}
        .scroll{overflow:auto;border:1px solid var(--line);border-radius:16px;background:#0e1725}
        table{width:100%;border-collapse:collapse;min-width:760px}
        th,td{text-align:left;padding:13px 15px;border-bottom:1px solid #202d40;vertical-align:top}
        th{position:sticky;top:0;background:#141f30;color:#aebdd0;font-size:11px;font-weight:750;letter-spacing:.09em;text-transform:uppercase;white-space:nowrap}
        td{color:#dce5f1;font-size:13px}
        tbody tr:last-child td{border-bottom:0}
        tbody tr:hover{background:#ffffff05}
        td a{margin:0}
        .Won,.Lost,.Pending{display:inline-flex;align-items:center;padding:3px 9px;border-radius:999px;font-size:11px;font-weight:750}
        .Won{background:#b9f36a18;color:var(--accent)}.Lost{background:#ff8f9118;color:var(--red)}.Pending{background:#f7ce7618;color:var(--amber)}
        details{max-width:600px}summary{color:var(--blue);cursor:pointer;font-weight:650}
        pre{overflow:auto;padding:22px;border:1px solid var(--line);border-radius:16px;background:#0b1220;color:#dce8f8;font:13px/1.75 ui-monospace,SFMono-Regular,Consolas,monospace;white-space:pre-wrap}
        .pagination{display:flex;align-items:center;gap:10px;margin-top:18px}
        .pagination a{padding:7px 12px;border:1px solid var(--line);border-radius:9px;background:var(--surface)}
        .hero{position:relative;overflow:hidden;padding:clamp(26px,5vw,56px);margin-bottom:25px;border:1px solid #344650;border-radius:26px;background:radial-gradient(ellipse at 95% 15%,#b9f36a24,transparent 34%),linear-gradient(120deg,#172536,#101a27 60%,#13231d)}
        .hero:after{content:"";position:absolute;right:8%;top:18%;width:190px;height:190px;border:1px solid #b9f36a25;border-radius:50%;box-shadow:0 0 0 24px #b9f36a09,0 0 0 48px #b9f36a06;pointer-events:none}
        .hero-content{position:relative;z-index:1;max-width:700px}
        .hero h1{max-width:660px;font-size:clamp(36px,6.5vw,70px)}
        .hero h1 span{color:var(--accent)}
        .hero p{max-width:560px;color:#aab8ca;font-size:16px}
        .hero .date-form{max-width:590px;margin-top:26px;padding:10px;border-color:#536048;background:#0b1220cc}
        .hero .date-form label{flex:1;min-width:180px;padding-left:8px}
        .hero .date-form button{white-space:nowrap}
        .grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:15px}
        .feature{display:flex;flex-direction:column;min-height:174px;padding:20px;color:var(--text)}
        .feature:hover{transform:translateY(-2px);border-color:#647a58;color:var(--text)}
        .feature .icon{display:grid;place-items:center;width:38px;height:38px;margin-bottom:15px;border:1px solid #3b4b3b;border-radius:12px;background:#b9f36a13;color:var(--accent);font-size:17px}
        .feature h2{margin-bottom:6px;font-size:16px}
        .feature p{margin:0;color:var(--muted);font-size:13px}
        .footer-note{margin-top:24px;color:var(--muted);font-size:12px}
        .error-panel{padding:clamp(22px,4vw,36px);border-color:#633f47;background:#21171d}
        @media(max-width:760px){main{padding-top:18px}.topbar{align-items:flex-start;flex-direction:column;margin-bottom:25px;padding-bottom:17px}nav{justify-content:flex-start;margin-left:-7px}.grid{grid-template-columns:1fr}.feature{min-height:130px}.hero:after{right:-70px;top:45%}form{align-items:stretch;flex-direction:column}form label,form input,form select,form button{width:100%}.notice{flex-direction:column;gap:5px}}
        @media(prefers-reduced-motion:reduce){*,*::before,*::after{scroll-behavior:auto!important;transition:none!important}}
        </style></head><body><main>
        <header class="topbar"><a class="brand" href="/?lang={{{langCode}}}"><span class="brand-mark">⚽</span><span>FOOTBALL AI<small>{{{Encode(Text(language, "Match intelligence"))}}}</small></span></a>
        <nav aria-label="{{{Encode(Text(language, "Main navigation"))}}}"><a href="/?lang={{{langCode}}}">{{{Encode(Text(language, "Overview"))}}}</a><a href="/predictions?lang={{{langCode}}}">{{{Encode(Text(language, "Predictions"))}}}</a>
        <a href="/tickets?lang={{{langCode}}}">{{{Encode(Text(language, "Tickets"))}}}</a><a href="/performance?lang={{{langCode}}}">{{{Encode(Text(language, "Performance"))}}}</a>
        <a href="/leagues?lang={{{langCode}}}">{{{Encode(Text(language, "Leagues"))}}}</a>
        <span class="language-switch"><a lang="en" href="?lang=en">EN</a><a lang="mk" href="?lang=mk">MK</a></span></nav></header>
        {{{(showNotice ? $"<p class=\"notice\"><strong>{Encode(Text(language, "SIMULATION ONLY"))}</strong><span>{Encode(Text(language, "1 unit is staked on the whole ticket; these are not actual payments. Source probabilities are not calibrated ticket probabilities. Pages display stored records only and do not collect or generate tickets."))}</span></p>" : "")}}}
        <section class="page-heading"><div class="eyebrow">{{{Encode(Text(language, "Football analytics"))}}}</div><h1>{{{Encode(Text(language, title))}}}</h1></section>
        {{{body}}}
        </main></body></html>
        """;
    }

    public static string Predictions(DateOnly date, IReadOnlyList<Match> matches, int page, int total,
        UiLanguage language = UiLanguage.English, string sort = "result-agreement", bool ascending = false)
    {
        string SortLabel(string key) => (key.StartsWith("result-", StringComparison.Ordinal) ? "1X2" :
            key.StartsWith("btts-", StringComparison.Ordinal) ? "BTTS" : Text(language, "Goals")) + " / " +
            Text(language, key.EndsWith("-agreement", StringComparison.Ordinal) ? "Agreement" : "Source average");
        string sortOptions = string.Concat(PredictionConsensus.SortKeys.Select(key =>
            $"<option value='{key}'{(key == sort ? " selected" : "")}>{Encode(SortLabel(key))}</option>"));
        var body = new StringBuilder($"""
            <form method="get"><input type="hidden" name="lang" value="{LanguageCode(language)}"><label>{Text(language, "Date")} <input type="date" name="date" value="{date:yyyy-MM-dd}" required></label>
            <label>{Text(language, "Sort by")}<select name="sort">{sortOptions}</select></label>
            <label>{Text(language, "Direction")}<select name="direction"><option value="desc"{(!ascending ? " selected" : "")}>{Text(language, "Descending")}</option><option value="asc"{(ascending ? " selected" : "")}>{Text(language, "Ascending")}</option></select></label>
            <button>{Text(language, "Show stored predictions")}</button></form>
            <p>{total} {Text(language, "stored fixtures for")} {date:yyyy-MM-dd}. {Text(language, "Latest snapshot per source; capture times are UTC.")}</p>
            <p class="muted">{Text(language, "Agreement is the share of voting sources, not a probability of winning. Source averages use only agreeing sources with valid market-specific percentages; missing percentages are excluded, never replaced with 0% or 50%. BTTS/goals source percentages are not currently stored, so their averages are n/a.")}</p>
            """);
        if (matches.Count == 0)
            body.Append($"<p>{Text(language, "No stored predictions for this date.")}</p>");
        else
        {
            body.Append($"<div class='scroll'><table><tr><th>{Text(language, "Fixture / league")}</th>{Header("1X2", "result")}{Header("BTTS", "btts")}{Header(Text(language, "Goals"), "goals")}<th>{Text(language, "Final / half-time")}</th><th>{Text(language, "Source details")}</th></tr>");
            foreach (var row in PredictionConsensus.Order(matches, sort, ascending))
            {
                var match = row.Match;
                var snapshots = CalibrationAnalyzer.LatestPerSource(match);
                body.Append("<tr>");
                Cell(body, $"{match.HomeTeam} - {match.AwayTeam} / {TicketPerformanceAnalyzer.LeagueName(match.League)}");
                VoteCell(row.Result);
                VoteCell(row.Btts);
                VoteCell(row.Goals);
                Cell(body, Score(match.Result, language));
                body.Append("<td>");
                if (snapshots.Count == 0)
                    body.Append(Text(language, "No stored snapshots."));
                else
                {
                    body.Append($"<details><summary>{Text(language, "Source details")} ({snapshots.Count})</summary><table><tr><th>{Text(language, "Source")}</th><th>1X2</th><th>{Text(language, "Score")}</th><th>BTTS</th><th>{Text(language, "Goals")}</th><th>{Text(language, "Source Prob.")}</th><th>{Text(language, "Captured UTC")}</th></tr>");
                    foreach (var snapshot in snapshots.OrderBy(x => x.Source))
                    {
                        body.Append("<tr>");
                        Cell(body, snapshot.Source);
                        Cell(body, snapshot.PredictedResult);
                        Cell(body, snapshot.PredictedScore);
                        Cell(body, snapshot.Btts);
                        Cell(body, snapshot.Goals);
                        Cell(body, snapshot.Confidence.HasValue ? snapshot.Confidence.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a");
                        Cell(body, snapshot.CapturedAtUtc.ToString("u"));
                        body.Append("</tr>");
                    }
                    body.Append("</table></details>");
                }
                body.Append("</td></tr>");
            }
            body.Append("</table></div>");
        }
        body.Append(Pagination($"/predictions?date={date:yyyy-MM-dd}&sort={Uri.EscapeDataString(sort)}&direction={(ascending ? "asc" : "desc")}", page, total, 100, language));
        return Page("Stored predictions", body.ToString(), language: language);

        string Header(string label, string market)
        {
            string Link(string key, string text)
            {
                string direction = key == sort && !ascending ? "asc" : "desc";
                return $"<a href='/predictions?date={date:yyyy-MM-dd}&amp;lang={LanguageCode(language)}&amp;sort={key}&amp;direction={direction}'>{Encode(text)}</a>";
            }
            return $"<th>{Text(language, "Consensus")} {Encode(label)}<br>{Link($"{market}-agreement", Text(language, "Agreement"))}<br>{Link($"{market}-probability", Text(language, "Source average"))}</th>";
        }

        void VoteCell(MarketConsensus consensus)
        {
            if (consensus.Pick == null)
            {
                Cell(body, Text(language, "n/a"));
                return;
            }
            string average = consensus.AverageProbability?.ToString("P1", CultureInfo.InvariantCulture) ?? Text(language, "n/a");
            body.Append("<td>").Append(Encode($"{consensus.Pick} ({consensus.Matching}/{consensus.Total})"))
                .Append("<br>").Append(Encode($"{Text(language, "Agreement")}: {consensus.Agreement:P1}"))
                .Append("<br><small>").Append(Encode($"{Text(language, "Source average")}: {average} — {consensus.Priced}/{consensus.Matching} {Text(language, "with probability")}"))
                .Append("</small></td>");
        }
    }

    public static string TicketTable(IEnumerable<TicketSettlement> settlements, UiLanguage language = UiLanguage.English)
    {
        var body = new StringBuilder($"<div class='scroll'><table><tr><th>{Text(language, "Ticket")}</th><th>{Text(language, "Date")}</th><th>{Text(language, "Mode")}</th><th>{Text(language, "Picks")}</th><th>{Text(language, "Status")}</th><th>{Text(language, "Odds")}</th><th>{Text(language, "Stake")}</th><th>{Text(language, "Profit")}</th><th>{Text(language, "Created UTC")}</th></tr>");
        foreach (var row in settlements)
        {
            body.Append($"<tr><td><a href='/tickets/{row.Ticket.Id}?lang={LanguageCode(language)}'>#{row.Ticket.Id}</a></td>");
            Cell(body, row.Ticket.MatchDate);
            Cell(body, row.Ticket.Mode);
            Cell(body, row.Selections.Count);
            body.Append($"<td class='{row.Status}'>{Text(language, row.Status.ToString())}</td>");
            Cell(body, Money(row.CombinedOdds, language));
            Cell(body, Money(row.Ticket.Stake, language));
            Cell(body, Money(row.Profit, language));
            Cell(body, row.Ticket.CreatedAtUtc.ToString("u"));
            body.Append("</tr>");
        }
        return body.Append("</table></div>").ToString();
    }

    public static string Ticket(TicketSettlement settlement, IReadOnlyDictionary<long, MatchResult> results, UiLanguage language = UiLanguage.English)
    {
        var body = new StringBuilder(TicketTable([settlement], language));
        body.Append($"<h2>{Text(language, "Frozen selections and current results")}</h2><div class='scroll'><table><tr><th>#</th><th>{Text(language, "Fixture / league")}</th><th>{Text(language, "Market / pick")}</th><th>{Text(language, "Odds")}</th><th>{Text(language, "Status")}</th><th>{Text(language, "FT / HT")}</th><th>{Text(language, "Details")}</th></tr>");
        foreach (var leg in settlement.Selections.OrderBy(x => x.Selection.Position))
        {
            var selection = leg.Selection;
            results.TryGetValue(selection.MatchId, out var result);
            string status = leg.Correct == true ? "Won" : leg.Correct == false ? "Lost" : "Pending";
            body.Append("<tr>");
            Cell(body, selection.Position);
            Cell(body, $"{selection.Fixture} / {TicketPerformanceAnalyzer.LeagueName(selection.League)}");
            Cell(body, $"{selection.Market} / {selection.Pick}");
            Cell(body, Money(selection.Odds, language));
            body.Append($"<td class='{status}'>{status}</td>");
            Cell(body, Score(result, language));
            body.Append($"<td><details><summary>{Text(language, "Evidence / pending reason")}</summary>");
            body.Append(Encode(selection.Evidence));
            body.Append("<p>").Append(Encode(Text(language, TicketPerformanceAnalyzer.PendingReason(leg, result) ?? ""))).Append("</p>");
            if (!TicketLedger.IsDailyMode(settlement.Ticket.Mode))
                body.Append($"<p>{Text(language, "Historical accuracy:")} {Encode(selection.Accuracy.ToString("P1"))}; {Text(language, "Wilson lower bound:")} {Encode(selection.Safe.ToString("P1"))}; {Text(language, "sample:")} {selection.Sample}</p>");
            body.Append($"<p>{Text(language, "Kickoff UTC:")} {Encode(selection.KickoffUtc.ToString("u"))}</p></details></td></tr>");
        }
        return Page($"{Text(language, "Ticket")} #{settlement.Ticket.Id}", body.Append("</table></div>").ToString(), language: language);
    }

    public static string RangeForm(DateOnly from, DateOnly to, UiLanguage language = UiLanguage.English) => $"""
        <form method="get">
        <input type="hidden" name="lang" value="{LanguageCode(language)}">
        <label>{Text(language, "From")} <input type="date" name="from" value="{from:yyyy-MM-dd}" required></label>
        <label>{Text(language, "To")} <input type="date" name="to" value="{to:yyyy-MM-dd}" required></label>
        <button>{Text(language, "Show stored records")}</button></form>
        """;

    public static string Pagination(string url, int page, int total, int size, UiLanguage language = UiLanguage.English) =>
        $"<div class='pagination'><span class='muted'>{Text(language, "Page")} {page} · {total} {Text(language, "records")}</span> " +
        (page > 1 ? $"<a href='{Encode(url)}&amp;page={page - 1}&amp;lang={LanguageCode(language)}'>{Text(language, "← Previous")}</a>" : "") +
        ((long)page * size < total ? $"<a href='{Encode(url)}&amp;page={page + 1}&amp;lang={LanguageCode(language)}'>{Text(language, "Next →")}</a>" : "") + "</div>";

    public static void Cell(StringBuilder body, object? value) =>
        body.Append("<td>").Append(Encode(value ?? "n/a")).Append("</td>");

    public static string Money(decimal? value, UiLanguage language = UiLanguage.English) =>
        value?.ToString("F2", CultureInfo.InvariantCulture) ?? Text(language, "n/a");

    public static string LanguageCode(UiLanguage language) => language == UiLanguage.Macedonian ? "mk" : "en";

    private static string Score(MatchResult? result, UiLanguage language) => result == null ? Text(language, "Pending") :
        $"FT {result.HomeGoals}-{result.AwayGoals} / HT " +
        (result.HalfTimeHomeGoals.HasValue && result.HalfTimeAwayGoals.HasValue
            ? $"{result.HalfTimeHomeGoals}-{result.HalfTimeAwayGoals}" : Text(language, "n/a"));
}
