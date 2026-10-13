using Microsoft.UI.Xaml;

namespace Neodash_Mod_Loader.UI;

/// <summary>Switches layout from the available control width, including navigation and window scaling.</summary>
public sealed partial class ElementWidthTrigger : StateTriggerBase
{
    public static readonly DependencyProperty TargetElementProperty = DependencyProperty.Register(
        nameof(TargetElement), typeof(FrameworkElement), typeof(ElementWidthTrigger), new PropertyMetadata(null, TargetChanged));
    public static readonly DependencyProperty MinWidthProperty = DependencyProperty.Register(
        nameof(MinWidth), typeof(double), typeof(ElementWidthTrigger), new PropertyMetadata(0d, BoundsChanged));
    public static readonly DependencyProperty MaxWidthProperty = DependencyProperty.Register(
        nameof(MaxWidth), typeof(double), typeof(ElementWidthTrigger), new PropertyMetadata(double.PositiveInfinity, BoundsChanged));

    public FrameworkElement? TargetElement
    {
        get => (FrameworkElement?)GetValue(TargetElementProperty);
        set => SetValue(TargetElementProperty, value);
    }

    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty);
        set => SetValue(MinWidthProperty, value);
    }

    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty);
        set => SetValue(MaxWidthProperty, value);
    }

    private static void TargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var trigger = (ElementWidthTrigger)sender;
        if (args.OldValue is FrameworkElement previous)
        {
            previous.SizeChanged -= trigger.TargetSizeChanged;
            previous.Loaded -= trigger.TargetLoaded;
            previous.Unloaded -= trigger.TargetUnloaded;
        }
        if (args.NewValue is FrameworkElement current)
        {
            current.SizeChanged += trigger.TargetSizeChanged;
            current.Loaded += trigger.TargetLoaded;
            current.Unloaded += trigger.TargetUnloaded;
        }
        trigger.Update();
    }

    private static void BoundsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((ElementWidthTrigger)sender).Update();
    private void TargetSizeChanged(object sender, SizeChangedEventArgs e) => Update();
    private void TargetLoaded(object sender, RoutedEventArgs e) => Update();
    private void TargetUnloaded(object sender, RoutedEventArgs e) => SetActive(false);

    private void Update()
    {
        // Exclusive upper bounds keep adjacent narrow/wide states mutually exclusive at the breakpoint.
        SetActive(TargetElement is { IsLoaded: true } element && element.ActualWidth >= MinWidth && element.ActualWidth < MaxWidth);
    }
}
