using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DormScoreViewer.Models;

/// <summary>
/// 插件全局配置（保存在插件配置目录的 Settings.json）。
/// </summary>
public class PluginSettings : ObservableObject
{
    private string _baseUrl = "http://10.132.11.5";
    private string _listPath = "/news/?list_42.html";
    private string _articleUrlPattern = "/news/?{0}.html";
    private string _trackedClasses = "";
    private int _timeoutSeconds = 12;
    private int _autoRefreshMinutes = 30;

    /// <summary>校园网站点根地址，结尾不要带斜杠。</summary>
    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    /// <summary>寝室内务栏目列表页路径。</summary>
    public string ListPath
    {
        get => _listPath;
        set => SetProperty(ref _listPath, value);
    }

    /// <summary>公告详情页 URL 模板，{0} 会被替换为文章 ID。</summary>
    public string ArticleUrlPattern
    {
        get => _articleUrlPattern;
        set => SetProperty(ref _articleUrlPattern, value);
    }

    /// <summary>关注的班级，用逗号/空格/分号分隔。匹配时会忽略空白与常见修饰（如「高一(1)班」与「1班」）。</summary>
    public string TrackedClasses
    {
        get => _trackedClasses;
        set => SetProperty(ref _trackedClasses, value);
    }

    /// <summary>单次 HTTP 请求超时秒数。</summary>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetProperty(ref _timeoutSeconds, value < 1 ? 1 : value);
    }

    /// <summary>组件自动刷新间隔（分钟），0 表示不自动刷新。</summary>
    public int AutoRefreshMinutes
    {
        get => _autoRefreshMinutes;
        set => SetProperty(ref _autoRefreshMinutes, value < 0 ? 0 : value);
    }

    /// <summary>
    /// 把 <see cref="TrackedClasses"/> 解析为归一化后的班级集合。
    /// </summary>
    public HashSet<string> GetTrackedClassSet()
    {
        var set = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(TrackedClasses)) return set;

        foreach (var part in TrackedClasses.Split(',', '，', ';', '；', ' ', '\t', '\n', '\r'))
        {
            var normalized = DormScoreService.NormalizeClassName(part);
            if (!string.IsNullOrEmpty(normalized)) set.Add(normalized);
        }
        return set;
    }
}
