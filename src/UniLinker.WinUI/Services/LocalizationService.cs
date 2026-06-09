using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.WinUI.Services;

/// <summary>
/// Simple localization service for English/Chinese support
/// </summary>
public partial class LocalizationService : ObservableObject
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private int _languageIndex = 0; // 0: English, 1: 中文
    public int LanguageIndex
    {
        get => _languageIndex;
        set
        {
            if (SetProperty(ref _languageIndex, value))
            {
                // Notify all string properties changed
                OnPropertyChanged(nameof(Settings));
                OnPropertyChanged(nameof(Dashboard));
                OnPropertyChanged(nameof(Devices));
                OnPropertyChanged(nameof(Share));
                OnPropertyChanged(nameof(General));
                OnPropertyChanged(nameof(Appearance));
                OnPropertyChanged(nameof(Language));
                OnPropertyChanged(nameof(Theme));
                OnPropertyChanged(nameof(Dark));
                OnPropertyChanged(nameof(Light));
                OnPropertyChanged(nameof(SystemRecommended));
                OnPropertyChanged(nameof(DeviceName));
                OnPropertyChanged(nameof(HttpPort));
                OnPropertyChanged(nameof(AutoStartOnLogin));
                OnPropertyChanged(nameof(MinimizeToTray));
                OnPropertyChanged(nameof(ScreenMirrorDefaults));
                OnPropertyChanged(nameof(DefaultResolution));
                OnPropertyChanged(nameof(DefaultFrameRate));
                OnPropertyChanged(nameof(DefaultBitrate));
                OnPropertyChanged(nameof(Plugins));
                OnPropertyChanged(nameof(ScreenMirror));
                OnPropertyChanged(nameof(LowLatencyScreenCapture));
                OnPropertyChanged(nameof(FileTransfer));
                OnPropertyChanged(nameof(ComingSoon));
                OnPropertyChanged(nameof(RemoteInput));
                OnPropertyChanged(nameof(About));
                OnPropertyChanged(nameof(ResetToDefaults));
                OnPropertyChanged(nameof(Save));
                OnPropertyChanged(nameof(SettingsSaved));
                OnPropertyChanged(nameof(SettingsReset));
                OnPropertyChanged(nameof(CloseUniLinker));
                OnPropertyChanged(nameof(MinimizeOrExit));
                OnPropertyChanged(nameof(MinimizeToTrayBtn));
                OnPropertyChanged(nameof(Exit));
                OnPropertyChanged(nameof(Cancel));
                OnPropertyChanged(nameof(RunningInBackground));
                OnPropertyChanged(nameof(ServerOnline));
                OnPropertyChanged(nameof(ShowWindow));
                OnPropertyChanged(nameof(Discovered));
                OnPropertyChanged(nameof(DevicesLower));
                OnPropertyChanged(nameof(Active));
                OnPropertyChanged(nameof(Connections));
                OnPropertyChanged(nameof(ActiveShares));
                OnPropertyChanged(nameof(Session));
                OnPropertyChanged(nameof(RecentDevices));
                OnPropertyChanged(nameof(Online));
                OnPropertyChanged(nameof(View));
                OnPropertyChanged(nameof(SystemReady));
                OnPropertyChanged(nameof(SearchDevices));
                OnPropertyChanged(nameof(Scan));
                OnPropertyChanged(nameof(AutoDiscover));
                OnPropertyChanged(nameof(LanDiscovery));
                OnPropertyChanged(nameof(ManualIp));
                OnPropertyChanged(nameof(Connect));
                OnPropertyChanged(nameof(ManualConnection));
                OnPropertyChanged(nameof(IpAddressPlaceholder));
                OnPropertyChanged(nameof(ScreenPreview));
                OnPropertyChanged(nameof(ReadyToShare));
                OnPropertyChanged(nameof(StartShare));
                OnPropertyChanged(nameof(StopShare));
                OnPropertyChanged(nameof(AdvancedSettings));
                OnPropertyChanged(nameof(Resolution));
                OnPropertyChanged(nameof(FrameRate));
                OnPropertyChanged(nameof(Bitrate));
                OnPropertyChanged(nameof(ShareStarted));
                OnPropertyChanged(nameof(ShareStopped));
                OnPropertyChanged(nameof(ShareConnected));
                OnPropertyChanged(nameof(ShareDisconnected));
                OnPropertyChanged(nameof(NotificationsEnabled));
                OnPropertyChanged(nameof(NotificationsDesc));
                OnPropertyChanged(nameof(ShareActive));
            }
        }
    }

    // Common strings
    public string Settings => LanguageIndex == 0 ? "Settings" : "设置";
    public string Dashboard => LanguageIndex == 0 ? "Dashboard" : "仪表盘";
    public string Devices => LanguageIndex == 0 ? "Devices" : "设备";
    public string Share => LanguageIndex == 0 ? "Share" : "分享";
    public string General => LanguageIndex == 0 ? "General" : "常规";
    public string Appearance => LanguageIndex == 0 ? "Appearance" : "外观";
    public string Language => LanguageIndex == 0 ? "Language" : "语言";
    public string Theme => LanguageIndex == 0 ? "Theme" : "主题";
    public string Dark => LanguageIndex == 0 ? "Dark" : "深色";
    public string Light => LanguageIndex == 0 ? "Light" : "浅色";
    public string SystemRecommended => LanguageIndex == 0 ? "System (Recommended)" : "跟随系统 (推荐)";
    public string DeviceName => LanguageIndex == 0 ? "Device Name" : "设备名称";
    public string HttpPort => LanguageIndex == 0 ? "HTTP Port" : "HTTP 端口";
    public string AutoStartOnLogin => LanguageIndex == 0 ? "Auto-start on login" : "开机自动启动";
    public string MinimizeToTray => LanguageIndex == 0 ? "Minimize to tray on close" : "关闭时最小化到托盘";
    public string ScreenMirrorDefaults => LanguageIndex == 0 ? "Screen Mirror Defaults" : "投屏默认设置";
    public string DefaultResolution => LanguageIndex == 0 ? "Default Resolution" : "默认分辨率";
    public string DefaultFrameRate => LanguageIndex == 0 ? "Default Frame Rate" : "默认帧率";
    public string DefaultBitrate => LanguageIndex == 0 ? "Default Bitrate" : "默认码率";
    public string Plugins => LanguageIndex == 0 ? "Plugins" : "插件";
    public string ScreenMirror => LanguageIndex == 0 ? "Screen Mirror" : "屏幕投屏";
    public string LowLatencyScreenCapture => LanguageIndex == 0 ? "Low-latency screen capture and streaming" : "低延迟屏幕捕获和流传输";
    public string FileTransfer => LanguageIndex == 0 ? "File Transfer" : "文件传输";
    public string ComingSoon => LanguageIndex == 0 ? "Coming soon" : "即将推出";
    public string RemoteInput => LanguageIndex == 0 ? "Remote Input" : "远程输入";
    public string About => LanguageIndex == 0 ? "About" : "关于";
    public string ResetToDefaults => LanguageIndex == 0 ? "Reset to Defaults" : "重置为默认";
    public string Save => LanguageIndex == 0 ? "Save" : "保存";
    public string SettingsSaved => LanguageIndex == 0 ? "Settings saved successfully" : "设置已保存";
    public string SettingsReset => LanguageIndex == 0 ? "Settings reset to defaults" : "设置已重置为默认值";
    public string CloseUniLinker => LanguageIndex == 0 ? "Close UniLinker?" : "关闭 UniLinker？";
    public string MinimizeOrExit => LanguageIndex == 0 ? "Do you want to minimize to system tray (background) or exit completely?" : "您想最小化到系统托盘（后台运行）还是完全退出？";
    public string MinimizeToTrayBtn => LanguageIndex == 0 ? "Minimize to tray" : "最小化到托盘";
    public string Exit => LanguageIndex == 0 ? "Exit" : "退出";
    public string Cancel => LanguageIndex == 0 ? "Cancel" : "取消";
    public string RunningInBackground => LanguageIndex == 0 ? "Running in background. Click to restore." : "正在后台运行。点击恢复窗口。";
    public string ServerOnline => LanguageIndex == 0 ? "Server Online" : "服务器在线";
    public string ShowWindow => LanguageIndex == 0 ? "Show Window" : "显示窗口";

    // Resolution strings
    public string Resolution1080p => LanguageIndex == 0 ? "1080p (1920x1080)" : "1080p (1920x1080)";
    public string Resolution2K => LanguageIndex == 0 ? "2K (2560x1440)" : "2K (2560x1440)";
    public string Resolution4K => LanguageIndex == 0 ? "4K (3840x2160)" : "4K (3840x2160)";

    // Frame rate strings
    public string Fps30 => LanguageIndex == 0 ? "30 fps" : "30 fps";
    public string Fps60 => LanguageIndex == 0 ? "60 fps" : "60 fps";

    // Bitrate strings
    public string Bitrate10M => LanguageIndex == 0 ? "10 Mbps" : "10 Mbps";
    public string Bitrate15M => LanguageIndex == 0 ? "15 Mbps" : "15 Mbps";
    public string Bitrate20M => LanguageIndex == 0 ? "20 Mbps" : "20 Mbps";

    // Dashboard strings
    public string Discovered => LanguageIndex == 0 ? "Discovered" : "已发现";
    public string DevicesLower => LanguageIndex == 0 ? "devices" : "设备";
    public string Active => LanguageIndex == 0 ? "Active" : "活跃";
    public string Connections => LanguageIndex == 0 ? "connections" : "连接";
    public string ActiveShares => LanguageIndex == 0 ? "Active Shares" : "活跃分享";
    public string Session => LanguageIndex == 0 ? "session" : "会话";
    public string RecentDevices => LanguageIndex == 0 ? "Recent Devices" : "最近设备";
    public string Online => LanguageIndex == 0 ? "Online" : "在线";
    public string View => LanguageIndex == 0 ? "View" : "查看";
    public string SystemReady => LanguageIndex == 0 ? "System Ready" : "系统就绪";

    // Devices page strings
    public string SearchDevices => LanguageIndex == 0 ? "Search devices..." : "搜索设备...";
    public string Scan => LanguageIndex == 0 ? "Scan" : "扫描";
    public string AutoDiscover => LanguageIndex == 0 ? "Auto-discover" : "自动发现";
    public string LanDiscovery => LanguageIndex == 0 ? "LAN Discovery" : "局域网发现";
    public string ManualIp => LanguageIndex == 0 ? "Manual IP" : "手动 IP";
    public string Connect => LanguageIndex == 0 ? "Connect" : "连接";
    public string ManualConnection => LanguageIndex == 0 ? "Manual Connection" : "手动连接";
    public string IpAddressPlaceholder => LanguageIndex == 0 ? "IP Address (e.g., 192.168.1.100)" : "IP 地址（例如：192.168.1.100）";

    // Share page strings
    public string ScreenPreview => LanguageIndex == 0 ? "Screen Preview" : "屏幕预览";
    public string ReadyToShare => LanguageIndex == 0 ? "Ready to share" : "准备分享";
    public string StartShare => LanguageIndex == 0 ? "Start Share" : "开始分享";
    public string StopShare => LanguageIndex == 0 ? "Stop Share" : "停止分享";
    public string AdvancedSettings => LanguageIndex == 0 ? "Advanced Settings" : "高级设置";
    public string Resolution => LanguageIndex == 0 ? "Resolution" : "分辨率";
    public string FrameRate => LanguageIndex == 0 ? "Frame Rate" : "帧率";
    public string Bitrate => LanguageIndex == 0 ? "Bitrate" : "码率";

    // Settings descriptions
    public string DeviceNameDesc => LanguageIndex == 0 ? "The name other devices will see when discovering this device" : "其他设备发现此设备时看到的名称";
    public string HttpPortDesc => LanguageIndex == 0 ? "Port for the local HTTP server (requires restart)" : "本地 HTTP 服务器端口（需要重启）";
    public string AutoStartDesc => LanguageIndex == 0 ? "Automatically start UniLinker when you log in to Windows" : "登录 Windows 时自动启动 UniLinker";
    public string MinimizeToTrayDesc => LanguageIndex == 0 ? "Minimize to system tray instead of closing" : "最小化到系统托盘而不是关闭";
    public string LanguageDesc => LanguageIndex == 0 ? "Choose your preferred language" : "选择您的首选语言";
    public string ThemeDesc => LanguageIndex == 0 ? "Choose the application theme" : "选择应用程序主题";
    public string DefaultResolutionDesc => LanguageIndex == 0 ? "Default resolution for screen sharing sessions" : "屏幕共享会话的默认分辨率";
    public string DefaultFrameRateDesc => LanguageIndex == 0 ? "Target frame rate for streaming" : "流传输的目标帧率";
    public string DefaultBitrateDesc => LanguageIndex == 0 ? "Video stream bitrate" : "视频流码率";
    public string FileTransferDesc => LanguageIndex == 0 ? "Drag and drop file sharing (Coming soon)" : "拖放文件共享（即将推出）";
    public string RemoteInputDesc => LanguageIndex == 0 ? "Control device remotely (Coming soon)" : "远程控制设备（即将推出）";

    // About
    public string Version => LanguageIndex == 0 ? "Cross-device low-latency screen sharing platform" : "跨设备低延迟屏幕共享平台";
    public string GitHubRepository => LanguageIndex == 0 ? "GitHub Repository" : "GitHub 仓库";
    public string MitLicense => LanguageIndex == 0 ? "MIT License" : "MIT 许可证";

    // Notifications
    public string ShareStarted => LanguageIndex == 0 ? "Screen sharing started" : "屏幕分享已开始";
    public string ShareStopped => LanguageIndex == 0 ? "Screen sharing stopped" : "屏幕分享已停止";
    public string ShareConnected => LanguageIndex == 0 ? "New viewer connected" : "新观众已连接";
    public string ShareDisconnected => LanguageIndex == 0 ? "Viewer disconnected" : "观众已断开";
    public string NotificationsEnabled => LanguageIndex == 0 ? "Enable notifications" : "启用通知";
    public string NotificationsDesc => LanguageIndex == 0 ? "Show notifications for share events" : "显示分享事件通知";
    public string ShareActive => LanguageIndex == 0 ? "Sharing Active" : "分享中";

    public void SetLanguage(int index)
    {
        LanguageIndex = index;
    }
}