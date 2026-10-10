using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.Helpers;
using DormScoreViewer.Models;

namespace DormScoreViewer.Services;

/// <summary>
/// 寝室扣分数据的抓取、解析、缓存与汇总服务。
/// </summary>
public class DormScoreService
{
    // ------------------------------------------------------------ 正则

    /// <summary>列表页中指向扣分公告的链接。</summary>
    private static readonly Regex ArticleLinkRegex = new(
        @"href=['""]\.\./news/\?(\d+)\.html['""][^>]*>([^<]*扣分[^<]*)<",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>文章正文中的表格。</summary>
    private static readonly Regex TableRegex = new(
        @"<table.*?</table>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>表格中的一行。</summary>
    private static readonly Regex RowRegex = new(
        @"<tr.*?</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>行中的单元格。</summary>
    private static readonly Regex CellRegex = new(
        @"<t[dh][^>]*>(.*?)</t[dh]>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Excel 导出表格中藏在 x:num 属性里的寝室号。</summary>
    private static readonly Regex NumRoomRegex = new(
        @"x:num=""(\d+(?:\.\d+)?)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Excel 导出表格中藏在 x:num 属性里的分数（可为负）。</summary>
    private static readonly Regex NumScoreRegex = new(
        @"x:num=""(-?\d+(?:\.\d+)?)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>HTML 标签。</summary>
    private static readonly Regex TagRegex = new(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>meta 标签中声明的字符集。</summary>
    private static readonly Regex MetaCharsetRegex = new(
        @"<meta[^>]*charset\s*=\s*['""]?\s*([a-zA-Z0-9\-_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 文章页中常见的发布时间写法：2026-10-08 12:30 / 2026/10/08 / 2026年10月08日。
    /// </summary>
    /// <remarks>
    /// 「日?」前必须用<strong>惰性</strong>的 <c>\s*?</c>：若写成贪婪的 <c>\s*</c>，
    /// 它会先把「日期与时间之间的空格」吃掉，随后可选的时间组因只剩数字而匹配失败，
    /// 且引擎不再回溯——结果是「2026-10-08 14:35」只能解析出日期、丢掉时分。
    /// </remarks>
    private static readonly Regex PublishFullDateRegex = new(
        @"(?<y>\d{4})\s*[-/年.]\s*(?<m>\d{1,2})\s*[-/月.]\s*(?<d>\d{1,2})\s*?日?" +
        @"(?:\s*[T ]\s*(?<hh>\d{1,2}):(?<mm>\d{2})(?::(?<ss>\d{2}))?)?",
        RegexOptions.Compiled);

    /// <summary>文章页/标题中只有月日时的写法：10月8日 / 10-08。</summary>
    private static readonly Regex PublishMonthDayRegex = new(
        @"(?<m>\d{1,2})\s*(?:月|[-/.])\s*(?<d>\d{1,2})\s*日?",
        RegexOptions.Compiled);

    /// <summary>候选编码，按顺序严格尝试。</summary>
    private static readonly string[] CandidateEncodings = { "GB2312", "GBK", "UTF-8" };

    // ------------------------------------------------------------ 状态

    private readonly object _cacheLock = new();

    /// <summary>文章 ID -> 扣分明细。</summary>
    private Dictionary<string, List<DormRecord>> _cache;

    private readonly string _cachePath;
    private readonly HttpClient _httpClient;

    /// <summary>最近一次更新的时间戳。</summary>
    public DateTime LastUpdated { get; private set; }

    /// <summary>缓存发生变化。</summary>
    public event EventHandler? CacheChanged;

    public DormScoreService(string pluginConfigFolder)
    {
        // 校园网页面多为 GB2312/GBK，.NET 默认不注册这些编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _cachePath = Path.Combine(pluginConfigFolder, "cache.json");
        _cache = ConfigureFileHelper.LoadConfig<Dictionary<string, List<DormRecord>>>(_cachePath)
                 ?? new Dictionary<string, List<DormRecord>>();

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    }

    // ------------------------------------------------------------ 抓取

    /// <summary>
    /// 抓取寝室内务栏目的公告列表，按时间从新到旧排序。
    /// </summary>
    public async Task<List<ArticleInfo>> FetchArticleListAsync(PluginSettings settings,
        CancellationToken cancellationToken = default)
    {
        var listUrl = CombineUrl(settings.BaseUrl, settings.ListPath);
        var html = await FetchHtmlAsync(listUrl, settings.TimeoutSeconds, cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<int>();
        var result = new List<ArticleInfo>();

        foreach (Match m in ArticleLinkRegex.Matches(html))
        {
            if (!int.TryParse(m.Groups[1].Value, out var id)) continue;
            if (!seen.Add(id)) continue;

            result.Add(new ArticleInfo { Id = id, Title = HtmlDecode(m.Groups[2].Value).Trim() });
        }

        // 栏目里的文章 ID 递增，按 ID 倒序即为按时间从新到旧
        result.Sort((a, b) => b.Id.CompareTo(a.Id));
        return result;
    }

    /// <summary>
    /// 抓取指定若干天的扣分数据。联网失败时自动回退本地缓存。
    /// </summary>
    /// <remarks>
    /// 与本类其它方法一致：不抛出异常，失败信息通过 <see cref="DayRecords.Error"/> 返回，
    /// 由调用方决定如何呈现——避免「全部失败」被伪装成「没有扣分记录」。
    /// </remarks>
    public async Task<List<DayRecords>> FetchDaysAsync(IEnumerable<ArticleInfo> articles, PluginSettings settings,
        CancellationToken cancellationToken = default)
    {
        var result = new List<DayRecords>();
        var cacheDirty = false;

        foreach (var article in articles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var day = new DayRecords { ArticleId = article.Id, Label = article.Title };
            var key = article.Id.ToString(CultureInfo.InvariantCulture);

            try
            {
                var url = CombineUrl(settings.BaseUrl,
                    string.Format(CultureInfo.InvariantCulture, settings.ArticleUrlPattern, article.Id));
                var html = await FetchHtmlAsync(url, settings.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
                var records = ParseArticle(html, article.Title);

                // 解析文章发布时间，供主界面「更新于」显示
                day.PublishedAt = ExtractPublishTime(html);

                if (records.Count > 0)
                {
                    day.Records = records;
                    lock (_cacheLock)
                    {
                        _cache[key] = records;
                    }
                    cacheDirty = true;
                }
                else
                {
                    // 抓到了页面但没解析出记录：可能是当天确实无扣分，也可能页面结构变了
                    day.Records = GetCached(key);
                    day.FromCache = day.Records.Count > 0;
                    if (day.Records.Count == 0)
                    {
                        day.Error = "未从页面中解析到扣分表格，可能是当日无扣分或页面结构已变化";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 联网失败：回退缓存，不让单篇失败中断整个批次
                day.Records = GetCached(key);
                day.FromCache = day.Records.Count > 0;
                day.Error = ex.Message;
            }

            result.Add(day);
        }

        if (cacheDirty) SaveCache();

        // 「最近一次更新」优先取本次抓到的最新一篇公告的发布时间，
        // 这样主界面显示的是数据本身的新鲜度，而非本地抓取时刻。
        var latestPublish = result
            .Where(d => d.PublishedAt.HasValue)
            .Max(d => d.PublishedAt!.Value);

        LastUpdated = result.Any(d => d.PublishedAt.HasValue) ? latestPublish : DateTime.Now;
        CacheChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>
    /// 抓取最近 <paramref name="days"/> 天的数据（自动获取列表并取最新的若干篇）。
    /// </summary>
    public async Task<List<DayRecords>> FetchRecentDaysAsync(int days, PluginSettings settings,
        CancellationToken cancellationToken = default)
    {
        List<ArticleInfo> articles;
        try
        {
            articles = await FetchArticleListAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 列表页都拿不到，直接抛出由上层显示具体原因
            throw;
        }

        if (articles.Count == 0)
        {
            throw new InvalidOperationException("未从列表页找到标题含「扣分」的公告，请检查栏目路径是否正确。");
        }

        var take = Math.Clamp(days, 1, Math.Max(1, articles.Count));
        return await FetchDaysAsync(articles.Take(take), settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> FetchHtmlAsync(string url, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        return DecodeHtml(bytes, response.Content.Headers.ContentType?.CharSet);
    }

    // ------------------------------------------------------------ 解析

    /// <summary>
    /// 解析公告正文里的扣分表格。
    /// </summary>
    /// <remarks>
    /// 与原 Python 脚本的重要差异：取单元格时用的是正则的<strong>完整匹配</strong>（含 <c>&lt;td ...&gt;</c> 标签本身），
    /// 因此能读到 Excel 导出表格里 <c>x:num="..."</c> 属性中的数字。原脚本只取捕获组（单元格内部文本），
    /// 属性被丢弃，导致 x:num 兜底从未生效——遇到「数字只藏在属性里」的公告会整表漏解析。
    /// </remarks>
    public static List<DormRecord> ParseArticle(string html, string dateLabel = "")
    {
        foreach (Match tableMatch in TableRegex.Matches(html))
        {
            var tableRecords = new List<DormRecord>();

            foreach (Match rowMatch in RowRegex.Matches(tableMatch.Value))
            {
                var cells = CellRegex.Matches(rowMatch.Value);
                if (cells.Count < 3) continue;

                var texts = cells.Select(c => HtmlDecode(CellText(c.Groups[1].Value))).ToList();

                // 跳过表头行
                if (IsHeaderRow(texts)) continue;

                var className = texts[0];
                var room = texts[1];
                var scoreText = texts[2];
                var reason = texts.Count > 3 ? texts[3] : "";

                // x:num 属性兜底（Excel 导出的表格常把数字只放在属性里）
                if (string.IsNullOrWhiteSpace(room))
                {
                    var m = NumRoomRegex.Match(cells[1].Value);
                    if (m.Success) room = m.Groups[1].Value;
                }
                if (string.IsNullOrWhiteSpace(scoreText))
                {
                    var m = NumScoreRegex.Match(cells[2].Value);
                    if (m.Success) scoreText = m.Groups[1].Value;
                }

                if (string.IsNullOrWhiteSpace(room)) continue;

                // 分数文本先做符号归一：网页里常见全角减号「－」、Unicode 减号「−」、
                // 不换行连字符等，double.TryParse 无法识别，会导致整行被静默丢弃。
                var scoreRaw = NormalizeMinus(scoreText.Trim());
                if (!double.TryParse(scoreRaw, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var score)
                    && !double.TryParse(scoreRaw, NumberStyles.Float, CultureInfo.CurrentCulture, out score))
                {
                    continue;
                }

                tableRecords.Add(new DormRecord
                {
                    DateLabel = dateLabel,
                    ClassName = className.Trim(),
                    Room = room.Trim(),
                    Score = score,
                    Reason = reason.Trim()
                });
            }

            if (tableRecords.Count > 0)
            {
                // 班级列在合并单元格时会留空，此处沿用上一行的班级
                PropagateClassName(tableRecords);
                return tableRecords;
            }
        }

        return new List<DormRecord>();
    }

    /// <summary>
    /// 把分数文本里的各种「减号」统一成 ASCII 半角负号 '-'
    /// （网页/ Excel 导出常见全角「－」、Unicode「−」、半角减号等）。
    /// </summary>
    public static string NormalizeMinus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim();
        // 各类减号/破折号统一替换
        return t.Replace('－', '-')   // 全角减号 U+FF0D
                .Replace('−', '-')   // Unicode 减号 U+2212
                .Replace('–', '-')   // en dash U+2013
                .Replace('—', '-')   // em dash U+2014
                .Replace('﹣', '-')   // 小型减号 U+FE63
                .Replace('－', '-');  // 表意减号 U+FF0D
    }

    private static void PropagateClassName(List<DormRecord> records)
    {
        var current = "";
        foreach (var r in records)
        {
            if (!string.IsNullOrWhiteSpace(r.ClassName)) current = r.ClassName;
            else r.ClassName = current;
        }
    }

    /// <summary>
    /// 判断是否为表头行：班级列含「班」且前两列含「寝室」。
    /// </summary>
    private static bool IsHeaderRow(IReadOnlyList<string> texts)
    {
        if (texts.Count < 2) return false;
        var first = texts[0];
        var joined = string.Concat(texts[0], texts[1]);
        return first.Contains("班") && joined.Contains("寝室");
    }

    /// <summary>
    /// 去掉单元格内的 HTML 标签（保留 x:num 等属性的读取由调用方用完整匹配另行处理）。
    /// </summary>
    private static string CellText(string rawHtml)
    {
        return TagRegex.Replace(rawHtml, "").Trim();
    }

    private static string HtmlDecode(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var s = System.Net.WebUtility.HtmlDecode(input);
        return s.Replace("\u00a0", " ");
    }

    // ------------------------------------------------------------ 发布时间解析

    /// <summary>
    /// 从文章页 HTML 中解析发布时间。
    /// </summary>
    /// <remarks>
    /// 先在页面靠前区域（&lt;head&gt; 与正文开头，发布时间一般在此）找「年-月-日 + 时:分」，
    /// 找不到再退化到只有月日的写法；月日会按「不晚于今天」的原则补全年份，
    /// 避免把 12 月的公告在 1 月解析成未来日期。
    /// </remarks>
    public static DateTime? ExtractPublishTime(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // 只扫描靠前的一段，兼顾准确与性能
        var scope = html.Length > 8000 ? html[..8000] : html;

        var full = PublishFullDateRegex.Match(scope);
        if (full.Success && TryBuildDate(full, out var exact))
        {
            return exact;
        }

        var md = PublishMonthDayRegex.Match(scope);
        if (md.Success && TryBuildDate(md, out var inferred))
        {
            return inferred;
        }

        return null;
    }

    private static bool TryBuildDate(Match m, out DateTime result)
    {
        result = default;

        if (!int.TryParse(m.Groups["d"].Value, out var day)) return false;

        var hasMonth = int.TryParse(m.Groups["m"].Value, out var month);
        if (!hasMonth) return false;

        var hasYear = m.Groups["y"].Success && int.TryParse(m.Groups["y"].Value, out var year);
        if (!hasYear)
        {
            // 只有月日：以「最近已过去的该月日」为准
            year = DateTime.Today.Year;
            if (month > DateTime.Today.Month ||
                (month == DateTime.Today.Month && day > DateTime.Today.Day))
            {
                year -= 1;
            }
        }

        var hour = 0;
        var minute = 0;
        var second = 0;
        if (m.Groups["hh"].Success) int.TryParse(m.Groups["hh"].Value, out hour);
        if (m.Groups["mm"].Success) int.TryParse(m.Groups["mm"].Value, out minute);
        if (m.Groups["ss"].Success) int.TryParse(m.Groups["ss"].Value, out second);

        try
        {
            result = new DateTime(year, month, day, hour, minute, second);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 编码

    /// <summary>
    /// 按「HTTP 头 -> meta 声明 -> 严格尝试候选编码 -> 宽松兜底」的顺序解码 HTML 字节。
    /// </summary>
    private static string DecodeHtml(byte[] bytes, string? headerCharset)
    {
        if (!string.IsNullOrWhiteSpace(headerCharset))
        {
            var fromHeader = TryDecodeStrict(bytes, headerCharset!);
            if (fromHeader != null) return fromHeader;
        }

        // 用 ASCII 读取头部，寻找 meta 声明
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var metaMatch = MetaCharsetRegex.Match(head);
        if (metaMatch.Success)
        {
            var fromMeta = TryDecodeStrict(bytes, metaMatch.Groups[1].Value);
            if (fromMeta != null) return fromMeta;
        }

        foreach (var name in CandidateEncodings)
        {
            var decoded = TryDecodeStrict(bytes, name);
            if (decoded != null) return decoded;
        }

        // 最后兜底：宽松 GB2312（不合法字节替换为占位符，不会抛异常）
        return Encoding.GetEncoding("GB2312").GetString(bytes);
    }

    private static string? TryDecodeStrict(byte[] bytes, string encodingName)
    {
        try
        {
            var enc = Encoding.GetEncoding(encodingName,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ExceptionFallback);
            return enc.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ 汇总

    /// <summary>
    /// 按班级汇总多日数据，按总扣分升序（扣得最多排最前）。
    /// </summary>
    /// <param name="days">多日数据。</param>
    /// <param name="trackedClasses">归一化后的关注班级集合，用于标记 <see cref="RoomAggregate.IsTracked"/>。</param>
    public static List<RoomAggregate> AggregateByRoom(IEnumerable<DayRecords> days, HashSet<string> trackedClasses)
    {
        var byRoom = new Dictionary<(string ClassName, string Room), RoomAggregate>();
        var daySet = new Dictionary<(string ClassName, string Room), HashSet<string>>();

        foreach (var day in days)
        {
            foreach (var r in day.Records)
            {
                var key = (r.ClassName, r.Room);
                if (!byRoom.TryGetValue(key, out var agg))
                {
                    agg = new RoomAggregate { ClassName = r.ClassName, Room = r.Room };
                    byRoom[key] = agg;
                    daySet[key] = new HashSet<string>();
                }

                agg.TotalScore += r.Score;
                agg.Count += 1;
                daySet[key].Add(day.Label);
                agg.IsTracked = trackedClasses.Contains(NormalizeClassName(r.ClassName));
            }
        }

        foreach (var kv in byRoom)
        {
            kv.Value.DayCount = daySet.TryGetValue(kv.Key, out var set) ? set.Count : 0;
        }

        return byRoom.Values
            .OrderBy(x => x.TotalScore)
            .ThenByDescending(x => x.Count)
            .ToList();
    }

    /// <summary>
    /// 按班级汇总多日数据，按总扣分升序。
    /// </summary>
    public static List<ClassAggregate> AggregateByClass(IEnumerable<DayRecords> days)
    {
        var byClass = new Dictionary<string, ClassAggregate>();

        foreach (var day in days)
        {
            foreach (var r in day.Records)
            {
                if (!byClass.TryGetValue(r.ClassName, out var agg))
                {
                    agg = new ClassAggregate { ClassName = r.ClassName };
                    byClass[r.ClassName] = agg;
                }

                agg.TotalScore += r.Score;
                agg.Count += 1;
            }
        }

        return byClass.Values.OrderBy(x => x.TotalScore).ToList();
    }

    // ------------------------------------------------------------ 工具

    /// <summary>
    /// 把寝室号规整为纯数字字符串（去掉前导零），便于比较。
    /// 例如 "1栋101"、"0101"、"101" 都会归一化为 "101"。
    /// </summary>
    public static string NormalizeRoom(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return s.Trim();

        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? digits : trimmed;
    }

    /// <summary>
    /// 把班级名规整为便于比较的形式：去掉空白、去掉「班」字后缀，
    /// 并把全角括号统一为半角。例如「高一(1)班」「高一（1）」「 高一 (1) 班 」
    /// 都会归一化为「高一(1)」。
    /// </summary>
    /// <remarks>
    /// 保留年级前缀（如「高一」），因为不同年级可能存在相同的班级序号。
    /// 用户若只填「1班」这类不含年级的写法，则不与「高一(1)」匹配——
    /// 这是有意为之，避免把高二/高三的 1 班一并命中。
    /// </remarks>
    public static string NormalizeClassName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var t = s.Trim();
        t = t.Replace("（", "(").Replace("）", ")");
        t = t.Replace(" ", "").Replace("\t", "").Replace("\u00a0", "");
        // 去掉结尾的「班」字
        if (t.EndsWith("班")) t = t[..^1];
        return t;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var b = (baseUrl ?? "").TrimEnd('/');
        var p = path ?? "";
        if (p.StartsWith("/")) return b + p;
        return b + "/" + p;
    }

    // ------------------------------------------------------------ 缓存

    private List<DormRecord> GetCached(string key)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(key, out var records)
                ? new List<DormRecord>(records)
                : new List<DormRecord>();
        }
    }

    private void SaveCache()
    {
        Dictionary<string, List<DormRecord>> snapshot;
        lock (_cacheLock)
        {
            snapshot = new Dictionary<string, List<DormRecord>>(_cache);
        }
        ConfigureFileHelper.SaveConfig(_cachePath, snapshot, true);
    }

    /// <summary>清空本地缓存。</summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache = new Dictionary<string, List<DormRecord>>();
        }
        SaveCache();
        CacheChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>当前缓存了多少天的数据。</summary>
    public int CachedDayCount
    {
        get
        {
            lock (_cacheLock) return _cache.Count;
        }
    }
}
