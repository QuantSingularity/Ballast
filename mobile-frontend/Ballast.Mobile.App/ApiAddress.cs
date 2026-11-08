namespace Ballast.Mobile.App;

public static class ApiAddress
{
    public const string PreferenceKey = "ballast.api_base_address";

    // Debug builds talk to a locally running API. Release builds must be pointed at an HTTPS endpoint,
    // either by editing ProductionAddress or by setting the preference at first launch.
    private const string ProductionAddress = "https://ballast.example.com";

    public static string Resolve()
    {
        var saved = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved)) return saved;
#if DEBUG
        return DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5000" : "http://localhost:5000";
#else
        return ProductionAddress;
#endif
    }
}
