using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using DormScoreViewer.Models;

namespace DormScoreViewer.Components;

/// <summary>
/// 「寝室扣分」组件的设置界面。纯代码构建，不用 .axaml。
/// 组件设置不需要 ComponentInfo 属性。
/// </summary>
public class DormScoreComponentSettings : ComponentBase<ComponentSettings>
{
    public DormScoreComponentSettings()
    {
        // 组件设置在挂载时注入，因此在 AttachedToVisualTree 时构建界面
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 防止重复构建
        if (Content != null) return;
        Build();
    }

    private void Build()
    {
        var cfg = Settings ?? new ComponentSettings();

        var panel = new StackPanel { Margin = new Thickness(8), Spacing = 8 };

        panel.Children.Add(Label("汇总天数（1-60）"));
        var daysBox = new TextBox { Text = cfg.RecentDays.ToString() };
        daysBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(daysBox.Text, out var v)) cfg.RecentDays = v;
        };
        panel.Children.Add(daysBox);

        panel.Children.Add(Label("轮播间隔（秒，0 为不轮播、一次列出全部）"));
        var rotateBox = new TextBox { Text = cfg.RotateSeconds.ToString() };
        rotateBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(rotateBox.Text, out var v)) cfg.RotateSeconds = v;
        };
        panel.Children.Add(rotateBox);

        panel.Children.Add(Label("字号（像素，0 为自动；常用 14 / 18 / 24 / 32）"));
        var fontBox = new TextBox { Text = cfg.FontSize.ToString("0.#") };
        fontBox.TextChanged += (_, _) =>
        {
            if (double.TryParse(fontBox.Text, out var v)) cfg.FontSize = v;
        };
        panel.Children.Add(fontBox);

        panel.Children.Add(MakeCheck("只显示关注班级的寝室", cfg.OnlyTrackedClasses, v => cfg.OnlyTrackedClasses = v));

        panel.Children.Add(Label("顶部内容开关（关掉可把高度让给扣分内容，避免被小字遮挡）"));
        panel.Children.Add(MakeCheck("显示标题「寝室扣分」", cfg.ShowTitle, v => cfg.ShowTitle = v));
        panel.Children.Add(MakeCheck("显示更新时间（扣分文章发布时间）", cfg.ShowUpdateTime, v => cfg.ShowUpdateTime = v));
        panel.Children.Add(MakeCheck("显示范围行（近 N 天 · 第 n/N 条）", cfg.ShowScope, v => cfg.ShowScope = v));

        panel.Children.Add(Label("内容显示"));
        panel.Children.Add(MakeCheck("显示扣分原因（在寝室号下方追加一行小字）", cfg.ShowReason, v => cfg.ShowReason = v));

        panel.Children.Add(new TextBlock
        {
            Text = "提示：默认只显示标题。若组件高度较小、扣分内容显示不全，可关闭上面两项，把高度让给内容。",
            FontSize = 11,
            Opacity = 0.55,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "关注的班级在插件设置页面填写，用逗号分隔（例如：高一(1)班, 高三(2)班）。",
            FontSize = 11,
            Opacity = 0.55,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        Content = new ScrollViewer { Content = panel };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.7
    };

    private static CheckBox MakeCheck(string content, bool initial, Action<bool> setter)
    {
        var box = new CheckBox { Content = content, IsChecked = initial };
        box.IsCheckedChanged += (_, _) => setter(box.IsChecked ?? initial);
        return box;
    }
}
