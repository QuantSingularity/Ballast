using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.MarketData;
using Ballast.Treasury;

namespace Ballast.Api.Services;

public sealed class FxService(
    BallastDb db, IRateProvider rates, BallastSettings settings, ForecastService forecasts, ModelHub models, AuditLog audit)
{
    private readonly object _hedgeLock = new();

    public IReadOnlyList<FxRateDto> Rates() =>
        Currencies.All.Where(c => c != "USD").Select(c => new FxRateDto(c, rates.SpotUsd(c), rates.AnnualRate(c),
            FxMath.ForwardUsd(rates, c, 30), FxMath.ForwardUsd(rates, c, 90), Math.Round(models.HighVolProbability(c), 4))).ToList();

    public ExposureDto Exposures()
    {
        var native = new Dictionary<string, double[]>();
        foreach (var ccy in forecasts.ForeignCurrencies())
            native[ccy] = forecasts.Forecast(forecasts.ResolveScope($"currency:{ccy}"), 91, 0.9, 12).Point;
        var lines = ExposureAggregator.Compute(native, settings.Today, db.Hedges.All(), rates, settings.BaseCurrency);
        return new ExposureDto(settings.Today, settings.BaseCurrency, lines);
    }

    public IReadOnlyList<HedgeRecommendation> Recommendations()
    {
        var lines = Exposures().Lines;
        var pHigh = lines.Select(l => l.Currency).Distinct().ToDictionary(c => c, models.HighVolProbability);
        return new HedgeAdvisor(models.Agent).Recommend(lines, pHigh);
    }

    public IReadOnlyList<HmmStatusDto> Regimes() =>
        Currencies.All.Where(c => c != "USD").Select(c =>
        {
            var h = models.Hmm(c);
            return new HmmStatusDto(c, h.Sigmas, h.Means, models.HighVolProbability(c), h.Transition[0, 0], h.Transition[1, 1]);
        }).ToList();

    public IReadOnlyList<HedgeDto> Hedges()
    {
        var lines = Exposures().Lines;
        return db.Hedges.All().OrderByDescending(h => h.TradeDate).ThenBy(h => h.Id, StringComparer.Ordinal)
            .Select(h => ToDto(h, lines)).ToList();
    }

    private HedgeDto ToDto(Hedge h, IReadOnlyList<ExposureLine> lines)
    {
        decimal mtmUsd = FxMath.HedgeMarkToMarketUsd(rates, h, settings.Today);
        decimal mtmBase = FxMath.Convert(rates, mtmUsd, "USD", settings.BaseCurrency);
        var eff = new EffectivenessResult(null, EffectivenessStatus.NotAssessable);
        int day = h.MaturityDate.DayNumber - settings.Today.DayNumber;
        var bucket = ExposureAggregator.Buckets.FirstOrDefault(b => day >= b.FromDay && day <= b.ToDay);
        if (h.Status == HedgeStatus.Active && bucket is not null)
        {
            var line = lines.FirstOrDefault(l => l.Currency == h.Currency && l.Bucket == bucket.Label);
            bool aligned = line is not null &&
                ((h.Direction == HedgeDirection.SellForeign && line.NetNative > 0) ||
                 (h.Direction == HedgeDirection.BuyForeign && line.NetNative < 0));
            if (aligned)
            {
                decimal fwd = FxMath.ForwardUsd(rates, h.Currency, day);
                decimal disc = 1 + (decimal)rates.AnnualRate("USD") * day / 360m;
                decimal sign = h.Direction == HedgeDirection.SellForeign ? 1 : -1;
                decimal itemChange = sign * Math.Abs(line!.NetNative) * (fwd - h.ContractRate) / disc;
                eff = HedgeEffectiveness.DollarOffset(mtmUsd, itemChange, 1m);
            }
        }
        return new HedgeDto(h.Id, h.Currency, h.Direction, h.Notional, h.ContractRate, h.TradeDate, h.MaturityDate,
            h.Counterparty, h.Status, Currencies.Round(mtmBase, settings.BaseCurrency),
            eff.Ratio is null ? null : Math.Round(eff.Ratio.Value, 4), eff.Status.ToString());
    }

    public HedgeDto BookHedge(CreateHedgeRequest req, Actor actor)
    {
        var ccy = (req.Currency ?? "").ToUpperInvariant();
        if (!Currencies.IsSupported(ccy) || ccy == "USD") throw new ValidationException("Currency must be one of EUR, GBP, JPY, CHF");
        if (req.Notional <= 0) throw new ValidationException("Notional must be greater than zero");
        if (req.TenorDays is < 1 or > 365) throw new ValidationException("TenorDays must be between 1 and 365");
        var rate = Math.Round(FxMath.ForwardUsd(rates, ccy, req.TenorDays), 6);
        Hedge hedge;
        lock (_hedgeLock)
        {
            string id = $"HDG-{db.Hedges.Count + 1:D4}";
            hedge = new Hedge(id, ccy, req.Direction, Math.Round(req.Notional, 0), rate, settings.Today,
                settings.Today.AddDays(req.TenorDays), string.IsNullOrWhiteSpace(req.Counterparty) ? "Treasury desk" : req.Counterparty.Trim(),
                HedgeStatus.Active);
            db.Hedges.Upsert(hedge);
        }
        audit.Append(actor.Name, "hedge.book", hedge.Id, $"{hedge.Direction} {hedge.Notional} {ccy} at {rate} maturing {hedge.MaturityDate}");
        db.SaveChanges();
        return ToDto(hedge, Exposures().Lines);
    }

    public HedgeDto CancelHedge(string id, Actor actor)
    {
        var hedge = db.Hedges.Get(id);
        if (hedge.Status != HedgeStatus.Active) throw new ConflictException($"Hedge is {hedge.Status}, only active hedges can be cancelled");
        var updated = hedge with { Status = HedgeStatus.Cancelled };
        db.Hedges.Upsert(updated);
        audit.Append(actor.Name, "hedge.cancel", id, "Hedge cancelled");
        db.SaveChanges();
        return ToDto(updated, Exposures().Lines);
    }
}
