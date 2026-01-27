using Microsoft.Extensions.Logging;
using JournalApp.Components.Services;
using MudBlazor.Services;

namespace JournalApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => 
            { 
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); 
            });

        // Add logging FIRST
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        
        // Register your services
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<AuthDatabase>();
        builder.Services.AddSingleton<UserStateService>();
        
        // Enable developer tools
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}