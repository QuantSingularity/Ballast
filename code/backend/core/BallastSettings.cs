using Ballast.Core.Domain;

namespace Ballast.Core;

public sealed class BallastSettings
{
    public const string PlaceholderSecret = "change-me";

    public string Environment { get; init; } = "development";
    public string BaseCurrency { get; init; } = "USD";
    public string JwtSecret { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 7;
    public int PasswordIterations { get; init; } = 210_000;
    public string? DataPath { get; init; }
    public string[] CorsOrigins { get; init; } = ["http://localhost:4200"];
    public DateOnly? AsOfDate { get; init; }
    public decimal DualApprovalThreshold { get; init; } = 50_000m;
    public decimal MaxBankConcentration { get; init; } = 0.35m;
    public decimal MinLiquidityBuffer { get; init; } = 10_000_000m;
    public int AuthRateLimitPerMinute { get; init; } = 30;
    public bool TrustProxy { get; init; }

    /// <summary>
    /// Networks whose X-Forwarded-* headers are honoured when <see cref="TrustProxy"/> is on. A request from any other
    /// address has its forwarded headers ignored, so a client that reaches the API directly cannot spoof its IP.
    /// Defaults to loopback and the private ranges that cluster ingress controllers and Docker networks use.
    /// </summary>
    public string[] TrustedProxyNetworks { get; init; } = DefaultTrustedProxyNetworks;

    public static readonly string[] DefaultTrustedProxyNetworks =
    [
        "127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"
    ];
    public string AiServiceUrl { get; init; } = "http://localhost:8000";
    public string DemoPassword { get; init; } = DefaultDemoPassword;

    public bool SeedDemoData => SeedDemoDataOverride ?? !IsProductionLike;
    public bool AllowSelfRegistration => AllowSelfRegistrationOverride ?? !IsProductionLike;
    public bool IsProductionLike => Environment is "production" or "staging";
    public bool? SeedDemoDataOverride { get; init; }
    public bool? AllowSelfRegistrationOverride { get; init; }
    public const string DefaultDemoPassword = "Ballast-Demo-2026!";
    public DateOnly Today => AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

    public static BallastSettings FromEnvironment(Func<string, string?> get)
    {
        static T Parse<T>(string? raw, T fallback, Func<string, T> parse) =>
            string.IsNullOrWhiteSpace(raw) ? fallback : parse(raw);

        var defaults = new BallastSettings();
        var env = (get("BALLAST_ENV") ?? "development").ToLowerInvariant();
        return new BallastSettings
        {
            Environment = env,
            BaseCurrency = (get("BALLAST_BASE_CURRENCY") ?? defaults.BaseCurrency).ToUpperInvariant(),
            JwtSecret = get("BALLAST_SECRET_KEY") ?? "",
            AccessTokenMinutes = Parse(get("BALLAST_ACCESS_TOKEN_MINUTES"), defaults.AccessTokenMinutes, int.Parse),
            RefreshTokenDays = Parse(get("BALLAST_REFRESH_TOKEN_DAYS"), defaults.RefreshTokenDays, int.Parse),
            PasswordIterations = Parse(get("BALLAST_PASSWORD_ITERATIONS"), defaults.PasswordIterations, int.Parse),
            DataPath = get("BALLAST_DATA_PATH"),
            CorsOrigins = Parse(get("BALLAST_CORS_ORIGINS"), defaults.CorsOrigins,
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            AsOfDate = Parse<DateOnly?>(get("BALLAST_AS_OF"), null, v => DateOnly.Parse(v)),
            DualApprovalThreshold = Parse(get("BALLAST_DUAL_APPROVAL_THRESHOLD"), defaults.DualApprovalThreshold, decimal.Parse),
            MaxBankConcentration = Parse(get("BALLAST_MAX_BANK_CONCENTRATION"), defaults.MaxBankConcentration, decimal.Parse),
            MinLiquidityBuffer = Parse(get("BALLAST_MIN_LIQUIDITY_BUFFER"), defaults.MinLiquidityBuffer, decimal.Parse),
            AuthRateLimitPerMinute = Parse(get("BALLAST_AUTH_RATE_LIMIT"), defaults.AuthRateLimitPerMinute, int.Parse),
            TrustProxy = Parse(get("BALLAST_TRUST_PROXY"), false, bool.Parse),
            TrustedProxyNetworks = Parse(get("BALLAST_TRUSTED_PROXIES"), defaults.TrustedProxyNetworks,
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            AiServiceUrl = (get("BALLAST_AI_SERVICE_URL") ?? defaults.AiServiceUrl).TrimEnd('/'),
            AllowSelfRegistrationOverride = Parse<bool?>(get("BALLAST_ALLOW_SELF_REGISTRATION"), null, v => bool.Parse(v)),
            DemoPassword = get("BALLAST_DEMO_PASSWORD") ?? defaults.DemoPassword,
            SeedDemoDataOverride = Parse<bool?>(get("BALLAST_SEED_DEMO_DATA"), null, v => bool.Parse(v))
        };
    }

    public void Validate()
    {
        if (!Uri.TryCreate(AiServiceUrl, UriKind.Absolute, out var ai) || ai.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"BALLAST_AI_SERVICE_URL must be an absolute http or https URL (got '{AiServiceUrl}')");
        if (!Currencies.IsSupported(BaseCurrency))
            throw new InvalidOperationException($"Unsupported base currency '{BaseCurrency}'");
        foreach (var cidr in TrustedProxyNetworks)
            if (!System.Net.IPNetwork.TryParse(cidr, out _))
                throw new InvalidOperationException($"BALLAST_TRUSTED_PROXIES entry '{cidr}' is not a valid CIDR network such as 10.0.0.0/8");
        if (TrustProxy && TrustedProxyNetworks.Length == 0)
            throw new InvalidOperationException("BALLAST_TRUST_PROXY is on but BALLAST_TRUSTED_PROXIES is empty, so no proxy could be trusted");
        if (IsProductionLike)
        {
            if (string.IsNullOrWhiteSpace(JwtSecret) || JwtSecret == PlaceholderSecret || JwtSecret.Length < 32)
                throw new InvalidOperationException(
                    "BALLAST_SECRET_KEY must be set to a value of at least 32 characters in production and staging");
            if (PasswordIterations < 100_000)
                throw new InvalidOperationException("BALLAST_PASSWORD_ITERATIONS must be at least 100000 in production and staging");
            if (SeedDemoData && DemoPassword == DefaultDemoPassword)
                throw new InvalidOperationException(
                    "Demo data cannot be seeded in production or staging with the default demo password. Set BALLAST_DEMO_PASSWORD or disable BALLAST_SEED_DEMO_DATA");
        }
        else if (string.IsNullOrWhiteSpace(JwtSecret) || JwtSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "BALLAST_SECRET_KEY must be at least 32 characters. Generate one with: openssl rand -hex 32");
        }
    }
}
