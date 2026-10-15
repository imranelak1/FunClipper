using System;
using System.Windows;
using System.Windows.Controls;

namespace FunClipper.Desktop;

public sealed class ConnectionWindow : Window
{
    public ConnectionWindow(ShareClient client)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Web connection · FunClipper"; Width = 500; Height = 350; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Web companion", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,16) });
        panel.Children.Add(new TextBlock { Text = "Server URL" });
        var url = new TextBox { Text = client.BaseUrl, Margin = new Thickness(0,5,0,14) }; panel.Children.Add(url);
        System.Windows.Automation.AutomationProperties.SetName(url, "Server URL");
        panel.Children.Add(new TextBlock { Text = "Access key" });
        var key = new PasswordBox { Password = client.Key, Padding = new Thickness(8), Margin = new Thickness(0,5,0,14) }; panel.Children.Add(key);
        System.Windows.Automation.AutomationProperties.SetName(key, "Access key");
        var status = new TextBlock { Text = "Local links work on this PC. A hosted HTTPS server gives friends access. The local companion supplies its key automatically.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12), FontSize = 12 }; panel.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var save = new Button { Content = "Save connection", HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(save);
        save.Click += (_, _) => { try { client.Configure(url.Text, key.Password); DialogResult = true; } catch (Exception ex) { status.Text = ex.Message; } };
    }
}
