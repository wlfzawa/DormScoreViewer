using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using DormScoreViewer.Services;

namespace DormScoreViewer.SettingsPages;

/// <summary>
/// 插件设置页面：配置数据源、关注的班级与网络参数。
/// </summary>
[SettingsPageInfo("dorm.score.viewer.settings", "寝室扣分")]
public partial class DormScoreSettingsPage : SettingsPageBase, INotifyPropertyChanged
{
    private string _statusText = "";
    private string _cacheInfoText = "";

    public DormScoreSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => UpdateCacheInfo();
    }

    // ------------------------------------------------------------ 绑定属性

    public string BaseUrl
    {
        get => Plugin.Settings.BaseUrl;
        set => Plugin.Settings.BaseUrl = value;
    }

    public string ListPath
    {
        get => Plugin.Settings.ListPath;
        set => Plugin.Settings.ListPath = value;
    }

    public string ArticleUrlPattern
    {
        get => Plugin.Settings.ArticleUrlPattern;
        set => Plugin.Settings.ArticleUrlPattern = value;
    }

    public string TrackedClasses
    {
        get => Plugin.Settings.TrackedClasses;
        set => Plugin.Settings.TrackedClasses = value;
    }

    public string TimeoutText
    {
        get => Plugin.Settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out var v)) Plugin.Settings.TimeoutSeconds = v;
        }
    }

    public string RefreshText
    {
        get => Plugin.Settings.AutoRefreshMinutes.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out var v)) Plugin.Settings.AutoRefreshMinutes = v;
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string CacheInfoText
    {
        get => _cacheInfoText;
        set { _cacheInfoText = value; OnPropertyChanged(); }
    }

    // ------------------------------------------------------------ 操作

    private async void OnTestConnectionClick(object? sender, RoutedEventArgs e)
    {
        StatusText = "正在测试连接…";

        try
        {
            var service = IAppHost.GetService<DormScoreService>();
            var articles = await service.FetchArticleListAsync(Plugin.Settings);

            StatusText = articles.Count > 0
                ? $"连接成功，共找到 {articles.Count} 条扣分公告。最新：{articles[0].Title}"
                : "连接成功，但没有找到标题含「扣分」的公告，请检查栏目路径是否正确。";

            UpdateCacheInfo();
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败：{ex.Message}";
        }
    }

    private void OnClearCacheClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IAppHost.GetService<DormScoreService>().ClearCache();
            StatusText = "本地缓存已清空。";
            UpdateCacheInfo();
        }
        catch (Exception ex)
        {
            StatusText = $"清空缓存失败：{ex.Message}";
        }
    }

    private void UpdateCacheInfo()
    {
        try
        {
            var service = IAppHost.GetService<DormScoreService>();
            CacheInfoText = $"本地已缓存 {service.CachedDayCount} 天的数据";
        }
        catch
        {
            CacheInfoText = "";
        }
    }

    // ------------------------------------------------------------ 绑定支持

    // 显式接口实现，避免隐藏 AvaloniaObject 自带的 PropertyChanged 事件
    private PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
