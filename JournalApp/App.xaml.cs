using Microsoft.Maui.Controls;

namespace JournalApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage())  // MainPage contains BlazorWebView
        {
            Title = "Journal Dashboard",
            Width = 1200,
            Height = 800
        };
    }
}