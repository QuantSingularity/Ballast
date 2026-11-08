namespace Ballast.AiModels.Config;

public static class AiModelsConfig
{
    public const int SeasonLength = 7;
    public const int DefaultSeed = 20260;
    public const int ValidationDays = 28;
    public const int MinHistoryDays = 120;
    public const int DefaultHorizonDays = 91;
    public const double DefaultConfidence = 0.90;
    public const int DefaultCalibrationOrigins = 20;
    public static readonly double[] HedgeRatioLevels = [0.0, 0.25, 0.5, 0.75, 1.0];
}
