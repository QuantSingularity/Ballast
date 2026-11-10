using System.Net.Http.Json;
using System.Text.Json;
using Ballast.Core;

namespace Ballast.AiClient;

/// <summary>
/// Typed client for the Python model service (code/ai_models). Calls are synchronous to fit the synchronous service
/// layer; the service caches repeated requests, and the API caches results, so each distinct computation runs once.
/// </summary>
public sealed class AiServiceClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ErrorBody(string? Code, string? Message);

    public ForecastResult Forecast(IReadOnlyList<double> series, DateOnly start, int horizon, double confidence, int origins) =>
        Post<ForecastResult>("/v1/forecast", new { series, startDate = start, horizon, confidence, origins });

    public BacktestMetrics Backtest(
        IReadOnlyList<double> series, DateOnly start, int horizon, int outerOrigins, int innerOrigins, double confidence) =>
        Post<BacktestMetrics>("/v1/forecast/backtest",
            new { series, startDate = start, horizon, outerOrigins, innerOrigins, confidence });

    public AnomalyFit FitAnomaly(IReadOnlyList<OutflowRecord> records) =>
        Post<AnomalyFit>("/v1/anomaly/fit", new { records });

    public IReadOnlyList<AnomalyAssessment> AssessBatch(IReadOnlyList<AssessItem> items) =>
        Post<AssessBatchResponse>("/v1/anomaly/assess-batch", new { items }).Results;

    public HmmParameters FitRegime(IReadOnlyList<double> returns) =>
        Post<HmmParameters>("/v1/regimes/fit", new { returns });

    public double[] FilterRegime(HmmParameters parameters, IReadOnlyList<double> returns) =>
        Post<FilterResponse>("/v1/regimes/filter", new { @params = parameters, returns }).Probabilities;

    public FxVarResult FxVar(
        HmmParameters parameters, IReadOnlyList<double> recentReturns, double exposureBase, int horizonDays, double confidence, int seed) =>
        Post<FxVarResult>("/v1/risk/fx-var",
            new { @params = parameters, recentReturns, exposureBase, horizonDays, confidence, seed });

    public HedgeRecommendations RecommendHedges(IReadOnlyList<HedgeRatioQuery> queries) =>
        Post<HedgeRecommendations>("/v1/hedging/recommend", new { queries });

    public ModelStatus Models() => Get<ModelStatus>("/v1/models");

    public bool IsHealthy(TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            using var res = http.Send(req, cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private sealed record AssessBatchResponse(List<AnomalyAssessment> Results);

    private sealed record FilterResponse(double[] Probabilities);

    private T Get<T>(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        return Execute<T>(req);
    }

    private T Post<T>(string path, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        return Execute<T>(req);
    }

    private T Execute<T>(HttpRequestMessage req)
    {
        HttpResponseMessage res;
        try
        {
            res = http.Send(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServiceUnavailableException(
                $"The AI model service is unreachable at {http.BaseAddress}. Start it with scripts/start_dev.sh or set BALLAST_AI_SERVICE_URL");
        }
        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<T>(res.Content.ReadAsStream(), Json)
                    ?? throw new BallastException("The AI model service returned an empty response", 502, "ai_service_error");
            }
            ErrorBody? error = null;
            try
            {
                error = JsonSerializer.Deserialize<ErrorBody>(res.Content.ReadAsStream(), Json);
            }
            catch (JsonException)
            {
            }
            string message = error?.Message ?? $"The AI model service returned status {(int)res.StatusCode}";
            if (res.StatusCode == System.Net.HttpStatusCode.Conflict && error?.Code == "model_not_fitted")
                throw new ModelNotFittedException(message);
            throw new BallastException(message, 502, "ai_service_error");
        }
    }
}
