using System.Windows.Markup;

namespace Pulse.Desktop;

public static class SelectorPalette
{
    public static readonly DependencyProperty HighlightProperty=DependencyProperty.RegisterAttached("Highlight",typeof(Brush),typeof(SelectorPalette));
    public static readonly DependencyProperty FocusProperty=DependencyProperty.RegisterAttached("Focus",typeof(Brush),typeof(SelectorPalette));
    public static Brush? GetHighlight(DependencyObject element)=>(Brush?)element.GetValue(HighlightProperty);
    public static void SetHighlight(DependencyObject element,Brush value)=>element.SetValue(HighlightProperty,value);
    public static Brush? GetFocus(DependencyObject element)=>(Brush?)element.GetValue(FocusProperty);
    public static void SetFocus(DependencyObject element,Brush value)=>element.SetValue(FocusProperty,value);
}

public static partial class Ui
{
    private static readonly SolidColorBrush selectorText=new(),selectorHighlight=new();
    private static readonly Lazy<ControlTemplate> selectorTemplate=new(()=>
        (ControlTemplate)XamlReader.Parse(SelectorTemplate));
    private static readonly Lazy<ControlTemplate> selectorItemTemplate=new(()=>
        (ControlTemplate)XamlReader.Parse(SelectorItemTemplate));

    public static ComboBox Select(IEnumerable<string> items,string selected,Action<string> changed)
    {
        var box=new ComboBox
        {
            SelectedValuePath="Tag",Background=Card,Foreground=selectorText,BorderBrush=BorderBrush,
            BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,14),Padding=new Thickness(8,6,8,6),
            MinWidth=140,MinHeight=30,MaxDropDownHeight=320,Template=selectorTemplate.Value,
            HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center
        };
        ApplySelectorBrushes(box);
        foreach(var item in items)
        {
            var option=new ComboBoxItem
            {
                Content=Option(item),Tag=item,Background=Card,Foreground=selectorText,BorderBrush=BorderBrush,
                BorderThickness=new Thickness(1),Padding=new Thickness(8,6,8,6),MinHeight=28,
                HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,
                Template=selectorItemTemplate.Value
            };
            ApplySelectorBrushes(option);box.Items.Add(option);
        }
        box.SelectedValue=selected;
        box.SelectionChanged+=(_,_)=>{if(box.SelectedValue is string value)changed(value);};
        return box;
    }

    private static void ApplySelectorBrushes(FrameworkElement element)
    {
        // Attached properties keep shared brushes live; WPF freezes resource dictionary values.
        SelectorPalette.SetHighlight(element,selectorHighlight);
        SelectorPalette.SetFocus(element,Accent);
    }

    private const string SelectorTemplate="""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:Pulse.Desktop;assembly=Pulse" TargetType="{x:Type ComboBox}">
          <Grid SnapsToDevicePixels="True">
            <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="28"/></Grid.ColumnDefinitions>
            <Border x:Name="SelectionSurface" Grid.ColumnSpan="2" CornerRadius="3"
                    Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"/>
            <ToggleButton Grid.ColumnSpan="2" Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
              <ToggleButton.Template><ControlTemplate TargetType="{x:Type ToggleButton}"><Border Background="Transparent"/></ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name="SelectionContent" IsHitTestVisible="False" Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                              ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"
                              TextElement.Foreground="{TemplateBinding Foreground}"/>
            <Path x:Name="DropDownArrow" Grid.Column="1" IsHitTestVisible="False" HorizontalAlignment="Center"
                  VerticalAlignment="Center" Fill="{TemplateBinding Foreground}" Data="M 0 0 L 8 0 L 4 4 Z"/>
            <Popup x:Name="PART_Popup" Grid.ColumnSpan="2" Placement="Bottom" AllowsTransparency="True"
                   Focusable="False" IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="None">
              <Border x:Name="DropDownSurface" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}"
                      Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="1" Padding="2" CornerRadius="3" SnapsToDevicePixels="True">
                <ScrollViewer CanContentScroll="True" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="SelectionSurface" Property="Background" Value="{Binding Path=(ui:SelectorPalette.Highlight), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="SelectionSurface" Property="BorderBrush" Value="{Binding Path=(ui:SelectorPalette.Focus), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
            <Trigger Property="IsDropDownOpen" Value="True"><Setter TargetName="SelectionSurface" Property="BorderBrush" Value="{Binding Path=(ui:SelectorPalette.Focus), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="DropDownArrow" Property="Opacity" Value="0.45"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """;

    private const string SelectorItemTemplate="""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ui="clr-namespace:Pulse.Desktop;assembly=Pulse" TargetType="{x:Type ComboBoxItem}">
          <Border x:Name="OptionSurface" Background="{TemplateBinding Background}" BorderBrush="Transparent"
                  BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}" CornerRadius="2"
                  SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              TextElement.Foreground="{TemplateBinding Foreground}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="OptionSurface" Property="Background" Value="{Binding Path=(ui:SelectorPalette.Highlight), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="OptionSurface" Property="Background" Value="{Binding Path=(ui:SelectorPalette.Highlight), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="OptionSurface" Property="Background" Value="{Binding Path=(ui:SelectorPalette.Highlight), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="OptionSurface" Property="BorderBrush" Value="{Binding Path=(ui:SelectorPalette.Focus), RelativeSource={RelativeSource TemplatedParent}}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """;
}
