namespace Pulse.Desktop;

public sealed partial class SettingsWindow
{
    private void AppearanceColors()
    {
        var config=app.Engine.Config;var colors=config.Appearance??=new();
        var presets=new WrapPanel();
        presets.Children.Add(Ui.Button("原版黑白",()=>ApplyColorPreset("original")));
        presets.Children.Add(Ui.Button("清新绿",()=>ApplyColorPreset("green")));
        presets.Children.Add(Ui.Button("蓝紫",()=>ApplyColorPreset("violet")));
        presets.Children.Add(Ui.Button("恢复默认配色",()=>ApplyColorPreset("default")));
        page.Children.Add(presets);
        page.Children.Add(Ui.Label("可点色块选色，或输入 #RRGGBB 后按 Enter、应用或离开输入框。留空使用默认颜色。",12,Ui.Muted));

        ColorSection("贴边悬浮条与详情卡");
        var progress=Ui.Check("收起条显示额度进度",colors.DockShowProgress,value=>{colors.DockShowProgress=value;app.SaveAppearance();});progress.Tag="dock-show-progress";page.Children.Add(progress);
        page.Children.Add(Ui.Label("进度长度跟随“显示剩余额度”；颜色沿用额度配色。关闭后恢复纯色收起条。",12,Ui.Muted));
        AddColorChoice("收起条配色","DockColorMode",[("usage","跟随额度"),("fixed","固定颜色")],colors.DockColorMode,value=>colors.DockColorMode=value);
        AddColor("收起条颜色","DockColor",()=>colors.DockColor,value=>colors.DockColor=value,()=>AppearancePalette.Resolve(null,config.Theme=="light"?"#202335":"#FFFFFF"));
        page.Children.Add(Ui.Label("跟随额度时只看各账号圆环当前显示的窗口，再取最高已用比例；无有效读数时使用收起条颜色。",12,Ui.Muted));
        AddColor("悬浮条背景色","RailBackground",()=>colors.RailBackground,value=>colors.RailBackground=value,()=>config.Theme=="light"?"#F5F6FA":"#000000");
        AddColor("详情卡背景色","DetailBackground",()=>colors.DetailBackground,value=>colors.DetailBackground=value,()=>config.Theme=="light"?"#FFFFFF":"#000000");

        ColorSection("额度变色");
        AddColorChoice("颜色变化方式","QuotaColorMode",[("thresholds","按阈值分档"),("gradient","随额度渐变"),("fixed","固定颜色")],colors.QuotaColorMode,value=>colors.QuotaColorMode=value);
        page.Children.Add(Ui.Label("颜色始终按已用比例判断，与显示剩余额度无关。账号单独设置的颜色优先；固定模式使用正常额度颜色。",12,Ui.Muted));
        AddColor("正常额度颜色","QuotaNormal",()=>colors.QuotaNormal,value=>colors.QuotaNormal=value,()=>config.Theme=="light"?"#202335":"#FFFFFF");
        AddColor("警告额度颜色","QuotaWarning",()=>colors.QuotaWarning,value=>colors.QuotaWarning=value,()=>"#FFB339");
        AddColor("危险额度颜色","QuotaCritical",()=>colors.QuotaCritical,value=>colors.QuotaCritical=value,()=>"#EF1630");
        AddColorThreshold("警告阈值（已用 %）","WarningPercent",true);
        AddColorThreshold("危险阈值（已用 %）","CriticalPercent",false);

        ColorSection("软件界面");
        AddColor("软件背景色","AppBackground",()=>colors.AppBackground,value=>colors.AppBackground=value,()=>config.Theme=="light"?"#F5F6FA":"#101318");
        AddColor("卡片背景色","AppSurface",()=>colors.AppSurface,value=>colors.AppSurface=value,()=>config.Theme=="light"?"#FFFFFF":"#1B2029");
        AddColor("软件强调色","AppAccent",()=>colors.AppAccent,value=>colors.AppAccent=value,()=>config.Theme=="light"?"#6552C9":"#B5ADFF");
        page.Children.Add(Ui.Label("文字会自动选择对比色。配色只影响软件界面，不改变导出报告的模板。",12,Ui.Muted));
    }

    private void ColorSection(string title)
    {
        var heading=Ui.Label(title,17);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,18,0,12);page.Children.Add(heading);
    }

    private void AddColorChoice(string title,string key,(string Value,string Label)[] options,string selected,Action<string> change)
    {
        page.Children.Add(Ui.Label(title,13,Ui.Muted));
        var label=options.FirstOrDefault(item=>item.Value==selected).Label??options[0].Label;
        var select=Ui.Select(options.Select(item=>item.Label),label,text=>{change(options.First(item=>item.Label==text).Value);app.SaveAppearance();});
        select.Tag="appearance:"+key;page.Children.Add(select);
    }

    private void AddColor(string title,string key,Func<string?> read,Action<string?> write,Func<string> fallback)
    {
        var group=new StackPanel{Tag="color-setting:"+key,Margin=new Thickness(0,0,0,8)};
        group.Children.Add(Ui.Label(title,13,Ui.Muted));
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var swatch=new Button{Tag="color-picker",Width=48,Height=35,Margin=new Thickness(0,0,9,8),BorderBrush=Ui.BorderBrush,BorderThickness=new Thickness(1),ToolTip=Localization.Translate("选择颜色"),Cursor=Cursors.Hand};
        var input=Ui.Field(read()??"");input.Tag="color-input";input.MinWidth=90;input.MaxLength=16;Grid.SetColumn(input,1);
        var error=Ui.Label("",12,Ui.Brush("#EF1630"));error.Tag="color-error";error.Visibility=Visibility.Collapsed;
        void Preview()=>swatch.Background=Ui.Brush(AppearancePalette.Resolve(read(),fallback()));
        void Commit()
        {
            string? value=null;
            if(!string.IsNullOrWhiteSpace(input.Text))
            {
                if(!AppearancePalette.TryNormalizeHex(input.Text,out var normalized))
                {error.Text=Localization.Translate("请输入 6 位十六进制颜色，例如 #5B8CFF；留空使用默认。" );error.Visibility=Visibility.Visible;return;}
                value=normalized;
            }
            error.Visibility=Visibility.Collapsed;
            if(read()!=value){write(value);app.SaveAppearance();}
            input.Text=value??"";Preview();
        }
        var apply=Ui.Button("应用",Commit);apply.Tag="color-apply";Grid.SetColumn(apply,2);
        var reset=Ui.Button("默认",()=>{input.Text="";Commit();});reset.Tag="color-default";Grid.SetColumn(reset,3);
        swatch.Click+=(_,_)=>
        {
            using var picker=new System.Windows.Forms.ColorDialog{FullOpen=true};
            var color=Ui.Brush(AppearancePalette.Resolve(read(),fallback())).Color;
            picker.Color=System.Drawing.Color.FromArgb(color.R,color.G,color.B);
            if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK){input.Text=$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";Commit();}
        };
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}};
        input.LostKeyboardFocus+=(_,_)=>Commit();
        row.Children.Add(swatch);row.Children.Add(input);row.Children.Add(apply);row.Children.Add(reset);group.Children.Add(row);group.Children.Add(error);page.Children.Add(group);Preview();
    }

    private void AddColorThreshold(string title,string key,bool warning)
    {
        var colors=app.Engine.Config.Appearance??=new();var thresholds=AppearancePalette.Thresholds(app.Engine.Config);
        var group=new StackPanel{Tag="color-setting:"+key};group.Children.Add(Ui.Label(title,13,Ui.Muted));
        var input=Ui.Field((warning?thresholds.Warning:thresholds.Critical).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture));input.Tag="color-input";
        var error=Ui.Label("",12,Ui.Brush("#EF1630"));error.Tag="color-error";error.Visibility=Visibility.Collapsed;
        void Commit()
        {
            var text=input.Text.Trim();var value=warning?75d:90d;
            var valid=text.Length==0||double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)||double.TryParse(text,out value);
            var other=AppearancePalette.Thresholds(app.Engine.Config);var low=warning?value:other.Warning;var high=warning?other.Critical:value;
            if(!valid||!double.IsFinite(value)||low<=0||low>=high||high>100)
            {error.Text=Localization.Translate("阈值须满足：0 < 警告 < 危险 ≤ 100。" );error.Visibility=Visibility.Visible;return;}
            error.Visibility=Visibility.Collapsed;
            colors.WarningPercent=low;colors.CriticalPercent=high;
            app.SaveAppearance();input.Text=value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
        }
        var apply=Ui.Button("应用",Commit);apply.Tag="color-apply";
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}};input.LostKeyboardFocus+=(_,_)=>Commit();
        group.Children.Add(Ui.Row(input,apply));group.Children.Add(error);page.Children.Add(group);
    }

    private void ApplyColorPreset(string name)
    {
        app.Engine.Config.Appearance=name switch
        {
            "green"=>new(){AppBackground="#101B19",AppSurface="#182724",AppAccent="#3BC6A5",RailBackground="#07120F",DetailBackground="#0B1814",QuotaNormal="#4ADE80",QuotaWarning="#FBBF24",QuotaCritical="#F87171"},
            "violet"=>new(){AppBackground="#141324",AppSurface="#201E36",AppAccent="#A78BFA",RailBackground="#100F1E",DetailBackground="#171529",QuotaNormal="#8B9CFF",QuotaWarning="#FBBF24",QuotaCritical="#F87171"},
            _=>new()
        };
        if(name!="default")app.Engine.Config.Theme="dark";
        app.SaveAppearance();Navigate("appearance");
    }
}
