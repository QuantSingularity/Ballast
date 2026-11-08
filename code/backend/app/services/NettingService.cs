using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.MarketData;
using Ballast.Treasury;

namespace Ballast.Api.Services;

public sealed class NettingService(BallastDb db, IRateProvider rates, BallastSettings settings, AuditLog audit, TimeProvider clock)
{
    private readonly object _lock = new();

    public IReadOnlyList<IntercompanyBalanceDto> Balances()
    {
        var names = db.Entities.All().ToDictionary(e => e.Id, e => e.Name);
        return db.IntercompanyBalances.All().Select(b => new IntercompanyBalanceDto(b.Id, b.LenderEntityId,
            names.GetValueOrDefault(b.LenderEntityId, b.LenderEntityId), b.BorrowerEntityId,
            names.GetValueOrDefault(b.BorrowerEntityId, b.BorrowerEntityId), b.Currency, b.Amount)).ToList();
    }

    private string Settlement(string? requested)
    {
        var ccy = (requested ?? settings.BaseCurrency).ToUpperInvariant();
        if (!Currencies.IsSupported(ccy)) throw new ValidationException($"Unsupported settlement currency '{ccy}'");
        return ccy;
    }

    public NettingResult Preview(string? settlementCurrency) =>
        MultilateralNetting.Run(db.IntercompanyBalances.All(), rates, Settlement(settlementCurrency));

    public NettingCycle Run(string? settlementCurrency, Actor actor)
    {
        NettingCycle cycle;
        lock (_lock)
        {
            var balances = db.IntercompanyBalances.All();
            if (balances.Count == 0) throw new ConflictException("There are no intercompany balances to net");
            var ccy = Settlement(settlementCurrency);
            var r = MultilateralNetting.Run(balances, rates, ccy);
            var transfers = r.Transfers.Select(t => t with { Amount = Currencies.Round(t.Amount, ccy) }).ToList();
            cycle = new NettingCycle($"NET-{db.NettingCycles.Count + 1:D4}", clock.GetUtcNow(), actor.Name, ccy,
                Currencies.Round(r.GrossVolume, ccy), transfers.Sum(t => t.Amount), transfers);
            db.NettingCycles.Upsert(cycle);
            foreach (var b in balances) db.IntercompanyBalances.Remove(b.Id);
        }
        audit.Append(actor.Name, "netting.run", cycle.Id,
            $"Gross {cycle.GrossVolume} {cycle.SettlementCurrency}, net {cycle.NetVolume}, {cycle.Transfers.Count} transfers");
        db.SaveChanges();
        return cycle;
    }

    public IReadOnlyList<NettingCycle> Cycles() => db.NettingCycles.All().OrderByDescending(c => c.RunAt).ToList();
}
