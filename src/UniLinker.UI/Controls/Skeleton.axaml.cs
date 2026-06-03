using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UniLinker.UI.Controls;

/// <summary>
/// 骨架屏组件 - 加载占位符，带 shimmer 动画
/// </summary>
public partial class Skeleton : UserControl
{
    public Skeleton()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // 不需要自定义 CornerRadius 属性，使用 UserControl 继承的即可
}
