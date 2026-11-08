using Ballast.Mobile.UI.Services;
using Microsoft.Extensions.Logging;

namespace Ballast.Mobile.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();
        builder.Services.AddBallastMobile(options => options.ApiBaseAddress = ApiAddress.Resolve());
        return builder.Build();
    }
}
