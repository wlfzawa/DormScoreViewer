using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using DormScoreViewer.Models;
using DormScoreViewer.Services;
using Microsoft.Extensions.Logging;

namespace DormScoreViewer.Components;

/// <summary>
/// 主界面组件：逐条轮播「关注班级」寝室的扣分情况。
/// 默认格式为「寝室号  扣分」（如 2114  -1）。
/// 用纯代码构建界面（不用 .axaml），彻底规避外部插件 XAML 编译/加载的不确定性。
/// </summary>
[ComponentInfo(
    "B7F3C1E4-5A2D-4E8B-9C61-3D0A7F82E5B1",
    "寝室扣分",
    "",
    "轮播显示关注班级寝室的扣分情况，格式为「寝室号 扣分」。点击组件手动刷新。")]
public class DormScoreComponent : ComponentBase<ComponentSettings>
{
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _scopeBlock;
    private readonly TextBlock _statusBlock;
    private readonly StackPanel _rowsPanel;

    /// <summary>标题行容器（含标题与更新时间），两者都隐藏时整行不占高度。</summary>
    private Grid? _headerPanel;

    // 轮播定时器（切条）与自动刷新定时器（下次抓取）
    private DispatcherTimer? _rotateTimer;
    private DispatcherTimer? _refreshTimer;
    private CancellationTokenSource? _cts;
    private bool _refreshing;

    // 当前要展示的条目（一次轮播一条）
    private List<RotationItem> _queue = new();
    private int _queueIndex;

    private static ILogger? Log => IAppHost.TryGetService<ILogger<DormScoreComponent>>();

    private ComponentSettings Config => Settings ?? new ComponentSettings();

    public DormScoreComponent()
    {
        (_titleBlock, _scopeBlock, _statusBlock, _rowsPanel) = BuildUi();

        // 点击组件手动刷新
        PointerPressed += (_, _) => _ = RefreshAsync();

        // 组件挂载到主界面时，ClassIsland 才注入 Settings（见 ComponentsService.GetComponent）。
        // 用 AttachedToVisualTree 触发首次刷新；同时挂 Loaded 作为后备，
        // 避免某些环境下 AttachedToVisualTree 早于订阅而错过，导致刷新根本不执行。
        AttachedToVisualTree += (_, _) => TryStart();
        Loaded += (_, _) => TryStart();
        DetachedFromVisualTree += OnDetached;
    }

    // ------------------------------------------------------------ 界面构建

    private (TextBlock, TextBlock, TextBlock, StackPanel) BuildUi()
    {
        var title = new TextBlock
        {
            Text = "寝室扣分",
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        var status = new TextBlock
        {
            Text = "点击刷新",
            FontSize = 11,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // 标题行：标题与更新时间各自独立显隐，两行都关时整行不占高度
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(status, 1);
        header.Children.Add(title);
        header.Children.Add(status);

        var scope = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // 逐条轮播的容器：每次只放一条记录
        var rows = new StackPanel { Spacing = 2 };

        // 最外层用竖向 StackPanel：所有区块按内容自然撑开。
        // 注意：绝不能用带「*」的行定义——在高度自适应的组件容器里，「*」行会塌缩为 0 高度，
        // 导致内容整块消失（这正是之前「只显示标题、不显示内容」的根因）。
        var root = new StackPanel { Margin = new Thickness(10, 6) };
        root.Children.Add(header);
        root.Children.Add(scope);
        root.Children.Add(rows);

        Content = root;
        _headerPanel = header;
        return (title, scope, status, rows);
    }

    /// <summary>
    /// 按设置应用顶部各元素的显示开关。
    /// </summary>
    /// <remarks>
    /// 顶部小字会占用组件的固定高度，把下方扣分内容挤出可视区。
    /// 这里让标题、更新时间、范围行三者都能独立关闭，把高度让给内容。
    /// 整行都隐藏时用 <see cref="IsVisible"/> 置 false，使其完全不参与布局（高度归零），
    /// 而不是仅把文字设空——空文字仍会占一行高度。
    /// </remarks>
    private void ApplyHeaderVisibility()
    {
        var cfg = Config;

        _titleBlock.IsVisible = cfg.ShowTitle;
        _statusBlock.IsVisible = cfg.ShowUpdateTime;

        // 标题与更新时间都关掉时，整行不占高度
        if (_headerPanel != null)
        {
            _headerPanel.IsVisible = cfg.ShowTitle || cfg.ShowUpdateTime;
        }

        _scopeBlock.IsVisible = cfg.ShowScope;
    }

    /// <summary>
    /// 构建一条「寝室号  扣分」。字号优先取用户设置，未设置（0）时按组件高度自适应。
    /// </summary>
    private Control MakeEntry(RotationItem item)
    {
        var size = ResolveFontSize();

        var room = new TextBlock
        {
            Text = item.Room,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var score = new TextBlock
        {
            Text = item.ScoreText,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            // 扣分数值与寝室号使用同一种颜色（继承默认前景色），
            // 避免红色在某些主题下刺眼或与背景对比不足。
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        // 寝室号、扣分、原因同处一行：列宽按Auto,Auto,*分配，
        // 原因占据剩余空间并在超出时以省略号截断，避免把扣分数字挤出可视区。
        var grid = new Grid
        {
            Opacity = item.IsTracked ? 1.0 : 0.8,
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*")
        };
        Grid.SetColumn(room, 0);
        Grid.SetColumn(score, 1);
        grid.Children.Add(room);
        grid.Children.Add(score);

        // 未开启「显示扣分原因」或该条没有原因时，只显示「寝室号 扣分」
        if (!Config.ShowReason || string.IsNullOrWhiteSpace(item.Reason))
        {
            return grid;
        }

        // 原因紧跟在扣分数字后面，同一行向右延伸。
        // 用较小字号并降低不透明度，让扣分主体依然第一眼可读。
        var reason = new TextBlock
        {
            Text = item.Reason,
            FontSize = Math.Max(10, size * 0.62),
            Opacity = 0.65,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        Grid.SetColumn(reason, 2);
        grid.Children.Add(reason);
        return grid;
    }

    /// <summary>
    /// 解析当前应使用的字号。用户设为 0（自动）时按组件高度估算，否则用用户值。
    /// </summary>
    private double ResolveFontSize()
    {
        var configured = Config.FontSize;
        if (configured > 1) return configured;

        // 自动：以组件实际高度推算，保证在 1~3 行高度内都清晰可读
        var h = Bounds.Height;
        if (h <= 0) return 20;

        // 组件高度减去顶部元素的实际占用，剩余给内容；轮播时一条为主
        var usable = Math.Max(24, h - ReservedTopHeight());

        // 原因与扣分同行显示，记录始终只占 1 行，故行高系数固定为 1.25。
        // （原因字号更小且垂直居中，不会增加行高）
        var size = usable / 1.25;
        return Math.Clamp(size, 14, 64);
    }

    /// <summary>
    /// 估算顶部元素（标题行 / 范围行 / 上下边距）实际占用的高度。
    /// 隐藏的元素不计入，从而把空间让给扣分内容。
    /// </summary>
    private double ReservedTopHeight()
    {
        var cfg = Config;
        var reserved = 12.0; // root 上下边距 6 + 6

        if (cfg.ShowTitle || cfg.ShowUpdateTime)
        {
            reserved += 18; // 标题行（12px 字号行高 + 余量）
        }

        if (cfg.ShowScope)
        {
            reserved += 17; // 范围行（11px 字号行高 + 间距）
        }

        return reserved;
    }

    // ------------------------------------------------------------ 生命周期

    private bool _started;

    private void TryStart()
    {
        if (_started) return;
        _started = true;

        _cts = new CancellationTokenSource();

        ApplyHeaderVisibility();
        UpdateScopeText();
        RestartRefreshTimer();
        RestartRotateTimer();

        Log?.LogInformation("寝室扣分组件已挂载，开始首次刷新");
        _ = RefreshAsync();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _rotateTimer?.Stop();
        _rotateTimer = null;
        _refreshTimer?.Stop();
        _refreshTimer = null;

        // 只取消、不 Dispose：避免与正在进行的刷新任务争用已释放的 Token
        _cts?.Cancel();
        _cts = null;
    }

    private void RestartRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;

        var minutes = Plugin.Settings.AutoRefreshMinutes;
        if (minutes <= 0) return;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(minutes)
        };
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
        _refreshTimer.Start();
    }

    private void RestartRotateTimer()
    {
        _rotateTimer?.Stop();
        _rotateTimer = null;

        var seconds = Config.RotateSeconds;
        if (seconds <= 0) return; // 0 表示不轮播

        _rotateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(seconds)
        };
        _rotateTimer.Tick += (_, _) => AdvanceRotation();
        _rotateTimer.Start();
    }

    /// <summary>切到下一条；到最后一条后回到第一条。</summary>
    private void AdvanceRotation()
    {
        if (_queue.Count <= 1) return;

        _queueIndex = (_queueIndex + 1) % _queue.Count;
        RenderCurrent();
        RefreshScopeWithCounter();
    }

    // ------------------------------------------------------------ 刷新

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            SetStatus("正在获取…", "");

            var service = IAppHost.GetService<DormScoreService>();
            var days = Config.RecentDays;
            var token = _cts?.Token ?? CancellationToken.None;

            var data = await service.FetchRecentDaysAsync(days, Plugin.Settings, token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() => ApplyData(data)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 组件已卸载，忽略
        }
        catch (Exception ex)
        {
            Log?.LogError(ex, "寝室扣分数据获取失败");
            SetStatusSafe("获取失败", $"无法获取扣分数据：{ex.Message}\n请确认已连接校园网，或在插件设置中检查数据源地址。");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplyData(List<DayRecords> days)
    {
        var cfg = Config;
        var tracked = Plugin.Settings.GetTrackedClassSet();

        // 关键防御：开启「只显示关注班级」但没配置任何班级时，自动回退显示全部，
        // 否则筛选结果恒为空，主界面永远空白。
        var noTrackedConfigured = tracked.Count == 0;
        var effectiveOnlyTracked = cfg.OnlyTrackedClasses && !noTrackedConfigured;

        var all = DormScoreService.AggregateByRoom(days, tracked);
        var shown = effectiveOnlyTracked ? all.Where(x => x.IsTracked).ToList() : all;

        // 本次抓取成功进入数据渲染，清空上一次的错误提示
        _lastEmpty = "";

        // 抓取过程中出现的单日失败，作为提示保留（不影响已有数据展示）
        var firstError = days.Select(d => d.Error).FirstOrDefault(e => !string.IsNullOrEmpty(e));
        var totalRecords = days.Sum(d => d.Records.Count);
        if (totalRecords == 0 && firstError != null)
        {
            _lastEmpty = $"未获取到扣分数据：{firstError}";
        }

        _queue = shown
            .Select(x => new RotationItem
            {
                Room = x.Room,
                ClassName = x.ClassName,
                Score = x.TotalScore,
                ScoreText = FormatScore(x.TotalScore),
                IsTracked = x.IsTracked,
                Reason = ReasonTextOf(days, x)
            })
            .ToList();

        // 数据刷新后从第一条重新开始，避免索引越界
        _queueIndex = 0;
        RenderCurrent();

        var cachedDays = days.Count(d => d.FromCache);
        var failedDays = days.Count(d => !string.IsNullOrEmpty(d.Error));

        ApplyHeaderVisibility();
        _statusBlock.Text = BuildStatusText(days, cachedDays, failedDays);

        UpdateScopeText();
    }

    /// <summary>
    /// 生成右上角状态文案。
    /// </summary>
    /// <remarks>
    /// 「更新于」显示的是<strong>扣分文章的发布时间</strong>（取所抓文章中最新的那篇），
    /// 而不是本地抓取时刻——后者每次刷新都会变，会让人误以为数据是刚发布的。
    /// 解析不到发布时间时才退回本地时刻。
    /// </remarks>
    private static string BuildStatusText(List<DayRecords> days, int cachedDays, int failedDays)
    {
        var published = days
            .Where(d => d.PublishedAt.HasValue)
            .Select(d => d.PublishedAt!.Value)
            .DefaultIfEmpty()
            .Max();

        string stamp;
        if (published > default)
        {
            // 同一天的文章不显示年份，避免「更新于 2026/10/08」过长挤掉标题
            stamp = published.Year == DateTime.Today.Year
                ? published.ToString("MM-dd HH:mm")
                : published.ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            stamp = DateTime.Now.ToString("HH:mm");
        }

        if (failedDays > 0) return $"更新于 {stamp}（{failedDays} 天失败）";
        if (cachedDays > 0) return $"更新于 {stamp}（{cachedDays} 天缓存）";
        return $"更新于 {stamp}";
    }

    /// <summary>
    /// 渲染当前这一条（含空态与错误提示）。
    /// </summary>
    private void RenderCurrent()
    {
        // 每次渲染都重算顶部显隐：开关是即时生效的，改完无需重启/刷新
        ApplyHeaderVisibility();

        _rowsPanel.Children.Clear();

        if (_queue.Count == 0)
        {
            _rowsPanel.Children.Add(new TextBlock
            {
                Text = EmptyText(),
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        _rowsPanel.Children.Add(MakeEntry(_queue[_queueIndex]));
    }

    private string _scopeBase = "";

    private string _lastEmpty = "";

    private void UpdateScopeText()
    {
        var cfg = Config;
        var tracked = Plugin.Settings.GetTrackedClassSet();
        var effectiveOnlyTracked = cfg.OnlyTrackedClasses && tracked.Count > 0;

        _scopeBase = effectiveOnlyTracked ? $"近 {cfg.RecentDays} 天 · 我的班级" : $"近 {cfg.RecentDays} 天 · 全部";
        RefreshScopeWithCounter();
    }

    /// <summary>范围文案附上「第 n/N 条」，让人知道正在轮播。</summary>
    private void RefreshScopeWithCounter()
    {
        if (_queue.Count > 1)
        {
            _scopeBlock.Text = $"{_scopeBase} · {_queueIndex + 1}/{_queue.Count}";
        }
        else
        {
            _scopeBlock.Text = _scopeBase;
        }
    }

    private string EmptyText()
    {
        // 刷新失败时优先显示真实原因，避免用户误以为「只是没有记录」
        if (_lastEmpty.Length > 0) return _lastEmpty;

        var cfg = Config;
        var tracked = Plugin.Settings.GetTrackedClassSet();
        if (tracked.Count == 0 && cfg.OnlyTrackedClasses)
        {
            return "未设置关注的班级，当前显示全部。可在插件设置中填写。";
        }
        return "所选范围内没有扣分记录。";
    }

    // ------------------------------------------------------------ 工具

    private void SetStatus(string status, string empty)
    {
        _statusBlock.Text = status;
        _lastEmpty = empty ?? "";
    }

    private void SetStatusSafe(string status, string empty)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SetStatus(status, empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => SetStatus(status, empty));
        }
    }

    private static string ReasonTextOf(List<DayRecords> days, RoomAggregate item)
    {
        var reasons = days
            .SelectMany(d => d.Records)
            .Where(r => r.ClassName == item.ClassName && r.Room == item.Room)
            .Select(r => r.Reason)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (reasons.Count == 0) return "";
        var first = reasons[0];
        return reasons.Count > 1 ? $"{first} 等 {reasons.Count} 项" : first;
    }

    /// <summary>
    /// 格式化扣分数值。要求：
    /// <list type="bullet">
    /// <item>非零即带负号（扣分语义），如 -1、-0.5；</item>
    /// <item>小数保留必要位数，不把 -0.5 截成 -0 或 -1；</item>
    /// <item>整数不显示多余小数点，如 -1 而不是 -1.0。</item>
    /// </list>
    /// </summary>
    private static string FormatScore(double score)
    {
        // 先按四舍五入规整到最多两位小数，消除浮点误差（如 0.30000000000000004）
        var rounded = Math.Round(score, 2, MidpointRounding.AwayFromZero);

        // 约等于 0（含 -0）时直接显示 0，不出现 "-0"
        if (Math.Abs(rounded) < 0.005) return "0";

        // 用自定义格式：整数不带小数点，小数最多两位且去掉末尾多余的 0
        // "0.##"：0 -> "0"，1 -> "1"，-1 -> "-1"，-0.5 -> "-0.5"，-1.50 -> "-1.5"
        var text = Math.Abs(rounded % 1) < 0.005
            ? Math.Abs(rounded).ToString("0", CultureInfo.InvariantCulture)
            : Math.Abs(rounded).ToString("0.##", CultureInfo.InvariantCulture);

        // 扣分统一显示为负数：正数也补上负号，符合「扣分」语义
        return "-" + text;
    }

    /// <summary>轮播队列中的一条。</summary>
    private class RotationItem
    {
        public string Room { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Reason { get; set; } = "";
        public double Score { get; set; }
        public string ScoreText { get; set; } = "";
        public bool IsTracked { get; set; }
    }
}
