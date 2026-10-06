using System.IO;
using System.Threading;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using DormScoreViewer.Components;
using DormScoreViewer.Models;
using DormScoreViewer.Services;
using DormScoreViewer.SettingsPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DormScoreViewer;

[PluginEntrance]
public class Plugin : PluginBase
{
    private static readonly object SettingsLock = new();

    /// <summary>插件全局配置。</summary>
    public static PluginSettings Settings { get; private set; } = new();

    /// <summary>插件配置目录绝对路径。</summary>
    public static string ConfigFolder { get; private set; } = "";

    /// <summary>设置文件的保存计时器（合并短时间内的多次改动）。</summary>
    private static Timer? _saveTimer;

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 输出一条可辨识的日志，便于确认插件是否真的被加载
        Console.WriteLine("[寝室扣分] 插件初始化中...");

        ConfigFolder = PluginConfigFolder;
        Directory.CreateDirectory(ConfigFolder);

        var settingsPath = Path.Combine(ConfigFolder, "Settings.json");
        Settings = ConfigureFileHelper.LoadConfig<PluginSettings>(settingsPath) ?? new PluginSettings();

        // 配置改动后延迟保存，避免连续输入时反复写盘
        Settings.PropertyChanged += (_, _) =>
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ =>
            {
                lock (SettingsLock)
                {
                    ConfigureFileHelper.SaveConfig(settingsPath, Settings, true);
                }
            }, null, 500, Timeout.Infinite);
        };

        // 核心服务（需要插件配置目录来存放缓存）
        services.AddSingleton<DormScoreService>(_ => new DormScoreService(ConfigFolder));

        // 主界面组件 + 组件设置界面
        services.AddComponent<DormScoreComponent, DormScoreComponentSettings>();

        // 插件设置页面
        services.AddSettingsPage<DormScoreSettingsPage>();

        Console.WriteLine("[寝室扣分] 插件初始化完成，已注册组件与设置页");
    }
}
