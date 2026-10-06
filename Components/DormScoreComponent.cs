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
/// 主界面组件：显示关注寝室的扣分情况。
/// 用纯代码构建界面（不用 .axaml），彻底规避外部插件 XAML 编译/加载的不确定性。
/// </summary>
[ComponentInfo(
    "B7F3C1E4-5A2D-4E8B-9C61-3D0A7F82E5B1",
    "寝室扣分",
    "",
    "显示关注寝室在校园网「寝室内务」栏目中的扣分情况，支持多日汇总。点击组件手动刷新。")]
public class DormScoreComponent : ComponentBase<ComponentSettings>
{
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _totalBlock;
    private readonly TextBlock _scopeBlock;
    private readonly TextBlock _statusBlock;
    private readonly TextBlock _emptyBlock;
    private readonly StackPanel _rowsPanel;

    private DispatcherTimer? _timer;
    private CancellationTokenSource? _cts;
    private bool _refreshing;

    private static ILogger? Log => IAppHost.TryGetService<ILogger<DormScoreComponent>>();

    private ComponentSettings Config => Settings ?? new ComponentSettings();

    public DormScoreComponent()
    {
        (_titleBlock, _totalBlock, _scopeBlock, _statusBlock, _emptyBlock, _rowsPanel) = BuildUi();

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

    private (TextBlock, TextBlock, TextBlock, TextBlock, TextBlock, StackPanel) BuildUi()
    {
        var title = new TextBlock { Text = "寝室扣分", FontSize = 12, Opacity = 0.6 };
        var status = new TextBlock
        {
            Text = "点击刷新",
            FontSize = 11,
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(status, 1);
        header.Children.Add(title);
        header.Children.Add(status);

        var total = new TextBlock
        {
            Text = "--",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Gray
        };
        var scope = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var summary = new Grid
        {
            Margin = new Thickness(0, 2, 0, 4),
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto")
        };
        Grid.SetColumn(total, 0);
        Grid.SetColumn(scope, 2);
        summary.Children.Add(total);
        summary.Children.Add(scope);

        var rows = new StackPanel { Spacing = 2 };

        var empty = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // 最外层用竖向 StackPanel：所有区块按内容自然撑开。
        // 注意：绝不能用带“*”的行定义——在高度自适应的组件容器里，“*”行会塌缩为 0 高度，
        // 导致扣分明细整块消失（这正是之前“只显示标题、不显示内容”的根因）。
        var root = new StackPanel
        {
            Margin = new Thickness(10, 8),
            Spacing = 0
        };
        root.Children.Add(header);
        root.Children.Add(summary);
        root.Children.Add(rows);
        root.Children.Add(empty);

        Content = root;
        return (title, total, scope, status, empty, rows);
    }

    private static Control MakeRow(RoomRowView item)
    {
        var room = new TextBlock
        {
            Text = item.DisplayRoom,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var cls = new TextBlock
        {
            Text = item.ClassName,
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var reason = new TextBlock
        {
            Text = item.Reason,
            FontSize = 11,
            Opacity = 0.75,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var score = new TextBlock
        {
            Text = item.ScoreText,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 0, 0, 0)
        };

        var grid = new Grid
        {
            Margin = new Thickness(0, 2),
            Opacity = item.RowOpacity,
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto")
        };
        Grid.SetColumn(room, 0);
        Grid.SetColumn(cls, 1);
        Grid.SetColumn(reason, 2);
        Grid.SetColumn(score, 3);
        grid.Children.Add(room);
        grid.Children.Add(cls);
        grid.Children.Add(reason);
        grid.Children.Add(score);
        return grid;
    }

    // ------------------------------------------------------------ 生命周期

    private bool _started;

    private void TryStart()
    {
        if (_started) return;
        _started = true;

        _cts = new CancellationTokenSource();

        var cfg = Config;
        var tracked = Plugin.Settings.GetTrackedRoomSet();
        _scopeBlock.Text = cfg.OnlyTrackedRooms && tracked.Count == 0
            ? $"近 {cfg.RecentDays} 天 · 全部（未设置关注寝室）"
            : cfg.OnlyTrackedRooms
                ? $"近 {cfg.RecentDays} 天 · 我的寝室"
                : $"近 {cfg.RecentDays} 天 · 全部";

        RestartTimer();
        Log?.LogInformation("寝室扣分组件已挂载，开始首次刷新");
        _ = RefreshAsync();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;

        // 只取消、不 Dispose：避免与正在进行的刷新任务争用已释放的 Token
        _cts?.Cancel();
        _cts = null;
    }

    private void RestartTimer()
    {
        _timer?.Stop();
        _timer = null;

        var minutes = Plugin.Settings.AutoRefreshMinutes;
        if (minutes <= 0) return;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(minutes)
        };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
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
        var tracked = Plugin.Settings.GetTrackedRoomSet();

        // 关键防御：开启「只显示关注寝室」但没配置任何寝室号时，自动回退显示全部，
        // 否则筛选结果恒为空，主界面永远空白。
        var noTrackedConfigured = tracked.Count == 0;
        var effectiveOnlyTracked = cfg.OnlyTrackedRooms && !noTrackedConfigured;

        var all = DormScoreService.AggregateByRoom(days, tracked);
        var shown = effectiveOnlyTracked ? all.Where(x => x.IsTracked).ToList() : all;

        _rowsPanel.Children.Clear();
        foreach (var item in shown.Take(cfg.MaxRows))
        {
            _rowsPanel.Children.Add(MakeRow(new RoomRowView
            {
                DisplayRoom = (item.IsTracked ? "★ " : "") + item.Room,
                ClassName = cfg.ShowClassName ? item.ClassName : "",
                Reason = cfg.ShowReason ? ReasonTextOf(days, item) : "",
                ScoreText = FormatScore(item.TotalScore),
                IsTracked = item.IsTracked,
                RowOpacity = item.IsTracked ? 1.0 : 0.75
            }));
        }

        var total = shown.Sum(x => x.TotalScore);
        _totalBlock.Text = FormatScore(total);
        _totalBlock.Foreground = total < 0 ? Brushes.IndianRed : Brushes.Gray;

        var cachedDays = days.Count(d => d.FromCache);
        var failedDays = days.Count(d => !string.IsNullOrEmpty(d.Error));

        _statusBlock.Text = failedDays > 0
            ? $"更新于 {DateTime.Now:HH:mm}（{failedDays} 天获取失败）"
            : cachedDays > 0
                ? $"更新于 {DateTime.Now:HH:mm}（{cachedDays} 天来自缓存）"
                : $"更新于 {DateTime.Now:HH:mm}";

        _scopeBlock.Text = effectiveOnlyTracked ? $"近 {cfg.RecentDays} 天 · 我的寝室" : $"近 {cfg.RecentDays} 天 · 全部";

        var firstError = days.Select(d => d.Error).FirstOrDefault(e => !string.IsNullOrEmpty(e));
        var totalRecords = days.Sum(d => d.Records.Count);

        if (_rowsPanel.Children.Count == 0)
        {
            _emptyBlock.Text = totalRecords == 0 && firstError != null
                ? $"未获取到扣分数据：{firstError}"
                : totalRecords == 0
                    ? "所选范围内没有扣分记录。"
                    : "所选范围内没有符合条件的寝室。";
        }
        else if (noTrackedConfigured)
        {
            _emptyBlock.Text = "未设置关注的寝室号，当前显示全部寝室。可在插件设置中填写。";
        }
        else if (failedDays > 0 && firstError != null)
        {
            _emptyBlock.Text = $"{failedDays} 天获取失败：{firstError}";
        }
        else
        {
            _emptyBlock.Text = "";
        }
    }

    // ------------------------------------------------------------ 工具

    private void SetStatus(string status, string empty)
    {
        _statusBlock.Text = status;
        _emptyBlock.Text = empty;
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

    private static string FormatScore(double score)
    {
        if (Math.Abs(score) < 0.0001) return "0";
        return score.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
