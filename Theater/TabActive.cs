using System.Windows;

namespace Theater;

/// <summary>
/// Attached property used to mark a tab button as active without
/// clobbering the button's <see cref="FrameworkElement.Tag"/> (which is
/// reserved for the tab identifier such as "video" / "gallery").
/// </summary>
public static class TabActive
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive",
        typeof(bool),
        typeof(TabActive),
        new PropertyMetadata(false));

    public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);
}
