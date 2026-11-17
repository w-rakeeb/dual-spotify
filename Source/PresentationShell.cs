using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using DualSpotify.UI;


public partial class App {
 StackPanel versionTools;
 void Responsive(){bool compact=window.ActualWidth<Theme.NarrowAt;railColumn.Width=new GridLength(compact?Theme.NarrowRail:Theme.Rail);brand.Visibility=compact?Visibility.Collapsed:Visibility.Visible;if(versionTools!=null)versionTools.Visibility=compact?Visibility.Collapsed:Visibility.Visible;foreach(var item in navigation)item.Compact(compact);}
 Button CaptionButton(string drawing,string name,Action action){
  var button=Ui.Button("",false,action);button.Style=Theme.Style("Caption");button.Width=Theme.CaptionButtonWidth;button.Height=Theme.CaptionHeight;button.MinWidth=Theme.CaptionButtonWidth;button.Padding=new Thickness(0);button.ToolTip=name;Ui.Name(button,name);
  var canvas=new Canvas{Width=Theme.CaptionIconSize,Height=Theme.CaptionIconSize};var path=new System.Windows.Shapes.Path{Data=Geometry.Parse(drawing),StrokeThickness=1,SnapsToDevicePixels=true};path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding("Foreground"){Source=button});canvas.Children.Add(path);button.Content=canvas;WindowChrome.SetIsHitTestVisibleInChrome(button,true);return button;
 }
 void BuildShell(){
  window=new Window{Title="Dual Spotify",Width=960,Height=680,MinWidth=800,MinHeight=520,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,UseLayoutRounding=true,SnapsToDevicePixels=true};
  window.SourceInitialized += (s, e) => DualSpotifyNative.SpotifyTaskbarIdentity.SetManagerWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle, Engine.Home);
  Theme.Bind(window,Control.BackgroundProperty,"bg.window");Theme.Bind(window,Control.FontFamilyProperty,"Font.App");WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=Theme.CaptionHeight,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12)});
  window.Icon=BitmapFrame.Create(new Uri(Path.Combine(Engine.Home,@"Assets\dual-spotify-green.ico"),UriKind.Absolute),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
  shell=new Grid();Theme.Bind(shell,Panel.BackgroundProperty,"bg.window");shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(Theme.CaptionHeight)});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(Theme.FooterHeight)});railColumn=new ColumnDefinition{Width=new GridLength(Theme.Rail)};shell.ColumnDefinitions.Add(railColumn);shell.ColumnDefinitions.Add(new ColumnDefinition());
  var rail=new Border{BorderThickness=new Thickness(0,0,1,0),IsHitTestVisible=false};Theme.Bind(rail,Border.BackgroundProperty,"bg.sidebar");Theme.Bind(rail,Border.BorderBrushProperty,"border.sidebar");Grid.SetRowSpan(rail,3);shell.Children.Add(rail);
  var caption=new Grid();caption.ColumnDefinitions.Add(new ColumnDefinition());caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetColumnSpan(caption,2);
  var identity=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(16,0,0,0),VerticalAlignment=VerticalAlignment.Center};identity.Children.Add(new Image{Source=BitmapFrame.Create(new Uri(Path.Combine(Engine.Home,@"Assets\dual-spotify.png"),UriKind.Absolute),BitmapCreateOptions.None,BitmapCacheOption.OnLoad),Width=Theme.BrandIconSize,Height=Theme.BrandIconSize,Margin=new Thickness(0,0,8,0)});
  brand=Text("Dual Spotify",12,"text.secondary",true);brand.TextWrapping=TextWrapping.NoWrap;brand.TextTrimming=TextTrimming.CharacterEllipsis;brand.MaxWidth=Theme.Rail-56;brand.VerticalAlignment=VerticalAlignment.Center;identity.Children.Add(brand);caption.Children.Add(identity);
  var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(CaptionButton("M1,6 L11,6","Minimize",()=>window.WindowState=WindowState.Minimized));var maximize=CaptionButton("M1.5,1.5 H10.5 V10.5 H1.5 Z","Maximize",()=>window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);controls.Children.Add(maximize);controls.Children.Add(CaptionButton("M2,2 L10,10 M10,2 L2,10","Close",()=>window.Close()));Grid.SetColumn(controls,1);caption.Children.Add(controls);shell.Children.Add(caption);
  window.StateChanged+=(s,e)=>{bool max=window.WindowState==WindowState.Maximized;var path=(System.Windows.Shapes.Path)((Canvas)maximize.Content).Children[0];path.Data=Geometry.Parse(max?"M3,3 V1 H11 V9 H9 M1,3 H9 V11 H1 Z":"M1.5,1.5 H10.5 V10.5 H1.5 Z");maximize.ToolTip=max?"Restore":"Maximize";Ui.Name(maximize,max?"Restore":"Maximize");};
  var menu=Stack();menu.Margin=new Thickness(0,12,0,0);Grid.SetRow(menu,1);shell.Children.Add(menu);foreach(var item in new[]{new[]{"Profiles","\uE716"},new[]{"Settings","\uE713"},new[]{"Files","\uE8B7"},new[]{"Updates","\uE895"}}){var nav=new NavItem(item[0],item[1]);string name=item[0];nav.Click+=(s,e)=>ShowPage(name);nav.PreviewKeyDown+=NavigationKeys;navigation.Add(nav);menu.Children.Add(nav);}
  var footer=new Border{BorderThickness=new Thickness(0,1,0,0)};Theme.Bind(footer,Border.BackgroundProperty,"bg.surface");Theme.Bind(footer,Border.BorderBrushProperty,"border.subtle");var footerGrid=new Grid();var leftWidth=new ColumnDefinition();leftWidth.SetBinding(ColumnDefinition.WidthProperty,new Binding("Width"){Source=railColumn});footerGrid.ColumnDefinitions.Add(leftWidth);footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
  var left=new Grid();left.ColumnDefinitions.Add(new ColumnDefinition());left.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var aboutNav=new NavItem("About","\uE946");aboutNav.Click+=(s,e)=>ShowPage("About");aboutNav.PreviewKeyDown+=NavigationKeys;navigation.Add(aboutNav);left.Children.Add(aboutNav);
  versionTools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};var version=Text("v"+BuildVersion,12,"text.muted");version.VerticalAlignment=VerticalAlignment.Center;versionTools.Children.Add(version);
  var update=Ui.IconButton("\uE72C","Check for updates",CheckUpdatesFromShortcut);update.Width=36;update.Height=36;update.MinWidth=36;update.FontSize=18;update.Padding=new Thickness(0);update.Margin=new Thickness(4,0,0,0);versionTools.Children.Add(update);Grid.SetColumn(versionTools,1);left.Children.Add(versionTools);footerGrid.Children.Add(left);
  var credit=new CreatorCredit(url=>OpenPath(url)){Margin=new Thickness(24,0,24,0)};Grid.SetColumn(credit,1);footerGrid.Children.Add(credit);footer.Child=footerGrid;Grid.SetRow(footer,2);Grid.SetColumnSpan(footer,2);shell.Children.Add(footer);
  content=new ContentControl{Margin=new Thickness(24,16,0,24),HorizontalContentAlignment=HorizontalAlignment.Stretch};pageScroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Style=Theme.Style("ThinScrollViewer"),Padding=new Thickness(0,0,24,0)};pageScroll.Margin=new Thickness(0,0,4,0);Grid.SetRow(pageScroll,1);Grid.SetColumn(pageScroll,1);shell.Children.Add(pageScroll);
  // A single quiet divider spans caption, body and footer.
  var line=new Border{Width=1,HorizontalAlignment=HorizontalAlignment.Right,IsHitTestVisible=false};Theme.Bind(line,Border.BackgroundProperty,"border.sidebar");Grid.SetRowSpan(line,3);shell.Children.Add(line);
  toast=new Toast();Grid.SetRow(toast,1);Grid.SetColumn(toast,1);shell.Children.Add(toast);
  updateNotice=new UpdateNotice();
  updateNotice.Install=()=>{ShowManager();ShowPage("Updates");};
  Grid.SetColumn(updateNotice,1);Grid.SetRowSpan(updateNotice,3);shell.Children.Add(updateNotice);
  var frame=new Border{Child=shell,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1)};Theme.Bind(frame,Border.BackgroundProperty,"bg.window");Theme.Bind(frame,Border.BorderBrushProperty,"border.subtle");window.Content=frame;
  window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Tab||e.Key==Key.Left||e.Key==Key.Right||e.Key==Key.Up||e.Key==Key.Down)Theme.Resources["KeyboardFocusVisibility"]=Visibility.Visible;};window.PreviewMouseDown+=(s,e)=>Theme.Resources["KeyboardFocusVisibility"]=Visibility.Collapsed;window.SizeChanged+=(s,e)=>Responsive();
 }
 void NavigationKeys(object sender,KeyEventArgs e){if(e.Key!=Key.Up&&e.Key!=Key.Down)return;int index=navigation.IndexOf((NavItem)sender);index=(index+(e.Key==Key.Up?-1:1)+navigation.Count)%navigation.Count;ShowPage(navigation[index].Page);navigation[index].Focus();e.Handled=true;}
}
