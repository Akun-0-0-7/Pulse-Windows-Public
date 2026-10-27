using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;

namespace Pulse.Desktop;

public static class ScrollChrome
{
    public static readonly DependencyProperty HoverProperty=DependencyProperty.RegisterAttached("Hover",typeof(Brush),typeof(ScrollChrome));
    public static readonly DependencyProperty PressedProperty=DependencyProperty.RegisterAttached("Pressed",typeof(Brush),typeof(ScrollChrome));
    public static Brush? GetHover(DependencyObject element)=>(Brush?)element.GetValue(HoverProperty);
    public static void SetHover(DependencyObject element,Brush value)=>element.SetValue(HoverProperty,value);
    public static Brush? GetPressed(DependencyObject element)=>(Brush?)element.GetValue(PressedProperty);
    public static void SetPressed(DependencyObject element,Brush value)=>element.SetValue(PressedProperty,value);
}

public static partial class Ui
{
    // Binding to an ordinary object keeps these shared brushes outside WPF's
    // resource freezing path. Changing their colors updates existing controls.
    private sealed class ScrollBrushSet
    {
        public SolidColorBrush Normal {get;}=new();
        public SolidColorBrush Hover {get;}=new();
        public SolidColorBrush Pressed {get;}=new();
    }
    private static readonly ScrollBrushSet scrollBrushes=new();
    private static readonly Lazy<Style> settingsScrollStyle=new(CreateSettingsScrollStyle);

    public static void StyleSettingsScrollBars(FrameworkElement owner)=>owner.Resources[typeof(ScrollBar)]=settingsScrollStyle.Value;

    private static void UpdateScrollBrushes()
    {
        var ink=CommonForeground(background.Color,card.Color,false);
        Color Readable(Color seed,double minimum)
        {
            for(var step=0;step<=100;step++)
            {
                var candidate=Blend(seed,ink,step/100d);
                if(Math.Min(Contrast(candidate,background.Color),Contrast(candidate,card.Color))>=minimum)return candidate;
            }
            return ink;
        }
        scrollBrushes.Normal.Color=Readable(Blend(background.Color,card.Color,.5),3.15);
        scrollBrushes.Hover.Color=Readable(Blend(scrollBrushes.Normal.Color,accent.Color,.3),3.4);
        scrollBrushes.Pressed.Color=Readable(Blend(scrollBrushes.Normal.Color,accent.Color,.55),3.6);
    }

    private static Style CreateSettingsScrollStyle()
    {
        var resources=(ResourceDictionary)XamlReader.Parse(ScrollTemplates);
        var style=(Style)resources["SettingsScrollBar"];
        Binding Live(string path)=>new(path){Source=scrollBrushes,Mode=BindingMode.OneWay};
        style.Setters.Add(new Setter(Control.ForegroundProperty,Live(nameof(ScrollBrushSet.Normal))));
        style.Setters.Add(new Setter(ScrollChrome.HoverProperty,Live(nameof(ScrollBrushSet.Hover))));
        style.Setters.Add(new Setter(ScrollChrome.PressedProperty,Live(nameof(ScrollBrushSet.Pressed))));
        return style;
    }

    private const string ScrollTemplates="""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:ui="clr-namespace:Pulse.Desktop;assembly=Pulse">
          <PathGeometry x:Key="UpGlyph" Figures="M 0 4 L 4 0 L 8 4 Z"/>
          <PathGeometry x:Key="DownGlyph" Figures="M 0 0 L 8 0 L 4 4 Z"/>
          <PathGeometry x:Key="LeftGlyph" Figures="M 4 0 L 0 4 L 4 8 Z"/>
          <PathGeometry x:Key="RightGlyph" Figures="M 0 0 L 4 4 L 0 8 Z"/>
          <Style x:Key="ScrollArrow" TargetType="{x:Type RepeatButton}">
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="{x:Type RepeatButton}">
                  <Border Background="Transparent">
                    <Path x:Name="Glyph" Data="{TemplateBinding Content}" Fill="{TemplateBinding Foreground}"
                          HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Glyph" Property="Fill" Value="{Binding Path=(ui:ScrollChrome.Hover), RelativeSource={RelativeSource AncestorType={x:Type ScrollBar}}}"/></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Glyph" Property="Fill" Value="{Binding Path=(ui:ScrollChrome.Pressed), RelativeSource={RelativeSource AncestorType={x:Type ScrollBar}}}"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="ScrollPage" TargetType="{x:Type RepeatButton}">
            <Setter Property="Focusable" Value="False"/><Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter>
          </Style>
          <ControlTemplate x:Key="ScrollThumb" TargetType="{x:Type Thumb}">
            <Border x:Name="ThumbSurface" Margin="{TemplateBinding Padding}" CornerRadius="4" Background="{TemplateBinding Background}"/>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ThumbSurface" Property="Background" Value="{Binding Path=(ui:ScrollChrome.Hover), RelativeSource={RelativeSource AncestorType={x:Type ScrollBar}}}"/></Trigger>
              <Trigger Property="IsDragging" Value="True"><Setter TargetName="ThumbSurface" Property="Background" Value="{Binding Path=(ui:ScrollChrome.Pressed), RelativeSource={RelativeSource AncestorType={x:Type ScrollBar}}}"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
          <ControlTemplate x:Key="VerticalScroll" TargetType="{x:Type ScrollBar}">
            <Grid Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
              <Grid.RowDefinitions><RowDefinition Height="16"/><RowDefinition/><RowDefinition Height="16"/></Grid.RowDefinitions>
              <RepeatButton Style="{StaticResource ScrollArrow}" Foreground="{TemplateBinding Foreground}" Content="{StaticResource UpGlyph}" Command="{x:Static ScrollBar.LineUpCommand}"/>
              <Track x:Name="PART_Track" Grid.Row="1" Orientation="Vertical" IsDirectionReversed="True"
                     Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                     ViewportSize="{TemplateBinding ViewportSize}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="{x:Static ScrollBar.PageUpCommand}"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Template="{StaticResource ScrollThumb}" Background="{TemplateBinding Foreground}" Padding="3,2" MinHeight="20"/></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="{x:Static ScrollBar.PageDownCommand}"/></Track.IncreaseRepeatButton>
              </Track>
              <RepeatButton Grid.Row="2" Style="{StaticResource ScrollArrow}" Foreground="{TemplateBinding Foreground}" Content="{StaticResource DownGlyph}" Command="{x:Static ScrollBar.LineDownCommand}"/>
            </Grid>
          </ControlTemplate>
          <ControlTemplate x:Key="HorizontalScroll" TargetType="{x:Type ScrollBar}">
            <Grid Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
              <Grid.ColumnDefinitions><ColumnDefinition Width="16"/><ColumnDefinition/><ColumnDefinition Width="16"/></Grid.ColumnDefinitions>
              <RepeatButton Style="{StaticResource ScrollArrow}" Foreground="{TemplateBinding Foreground}" Content="{StaticResource LeftGlyph}" Command="{x:Static ScrollBar.LineLeftCommand}"/>
              <Track x:Name="PART_Track" Grid.Column="1" Orientation="Horizontal" IsDirectionReversed="False"
                     Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                     ViewportSize="{TemplateBinding ViewportSize}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="{x:Static ScrollBar.PageLeftCommand}"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Template="{StaticResource ScrollThumb}" Background="{TemplateBinding Foreground}" Padding="2,3" MinWidth="20"/></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="{x:Static ScrollBar.PageRightCommand}"/></Track.IncreaseRepeatButton>
              </Track>
              <RepeatButton Grid.Column="2" Style="{StaticResource ScrollArrow}" Foreground="{TemplateBinding Foreground}" Content="{StaticResource RightGlyph}" Command="{x:Static ScrollBar.LineRightCommand}"/>
            </Grid>
          </ControlTemplate>
          <Style x:Key="SettingsScrollBar" TargetType="{x:Type ScrollBar}">
            <Setter Property="OverridesDefaultStyle" Value="True"/><Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Width" Value="{DynamicResource {x:Static SystemParameters.VerticalScrollBarWidthKey}}"/>
            <Setter Property="Template" Value="{StaticResource VerticalScroll}"/>
            <Style.Triggers>
              <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="Height" Value="{DynamicResource {x:Static SystemParameters.HorizontalScrollBarHeightKey}}"/>
                <Setter Property="Template" Value="{StaticResource HorizontalScroll}"/>
              </Trigger>
            </Style.Triggers>
          </Style>
        </ResourceDictionary>
        """;
}
