using System.Text.Json;
using Ballast.Mobile.UI.Services;

namespace Ballast.Mobile.App;

public sealed class SecureTokenStore : ITokenStore
{
    private const string Key = "ballast.session";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StoredSession?> LoadAsync()
    {
        try
        {
            var raw = await SecureStorage.Default.GetAsync(Key);
            return string.IsNullOrEmpty(raw) ? null : JsonSerializer.Deserialize<StoredSession>(raw, Json);
        }
        catch (Exception)
        {
            // The keystore can become unreadable after a device restore or biometric change. Start signed out.
            SecureStorage.Default.Remove(Key);
            return null;
        }
    }

    public Task SaveAsync(StoredSession session) =>
        SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(session, Json));

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
