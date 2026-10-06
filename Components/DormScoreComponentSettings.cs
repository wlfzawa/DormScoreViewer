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

        panel.Children.Add(Label("最多显示行数（1-50）"));
        var rowsBox = new TextBox { Text = cfg.MaxRows.ToString() };
        rowsBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(rowsBox.Text, out var v)) cfg.MaxRows = v;
        };
        panel.Children.Add(rowsBox);

        panel.Children.Add(MakeCheck("只显示关注的寝室", cfg.OnlyTrackedRooms, v => cfg.OnlyTrackedRooms = v));
        panel.Children.Add(MakeCheck("显示班级", cfg.ShowClassName, v => cfg.ShowClassName = v));
        panel.Children.Add(MakeCheck("显示扣分原因", cfg.ShowReason, v => cfg.ShowReason = v));

        panel.Children.Add(new TextBlock
        {
            Text = "关注的寝室号在插件设置页面填写，用逗号分隔。",
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
