# Usage walkthroughs

All scenarios use the demo data (development only). Sign in at http://localhost:4200.

## 1. Review the cash and liquidity picture

1. **Dashboard** shows total cash, the 30-day projection, liquidity coverage, cash at risk, covenant watch and payments
   awaiting approval, with the 91-day projected balance and its interval.
2. **Cash positions** lists accounts; click one to see its last 30 days of transactions. Savings and pool accounts have none by design.
3. **Forecast** lets you change scope (group or a currency), horizon and confidence. The backtest table at the bottom
   shows how the model has performed against a seasonal-naive baseline and how often the intervals held.

## 2. Hedge FX exposure (sign in as `treasurer@ballast.example`)

1. **FX and hedging** shows forecast net exposure by currency and horizon, how much is hedged, and what is open.
2. **Recommendations** gives a target hedge ratio per bucket and the notional to trade. The agent's view depends on the
   volatility regime detected for that currency; the result is clamped to the policy band.
3. **Book a forward** with a currency, direction, notional and tenor. The contract rate is the forward rate at booking, so mark to
   market starts at zero.
4. The hedge book shows mark to market and an effectiveness badge. "Ineffective" here means hedge notional is outside 80-125%
   of the forecast exposure it is meant to cover, not that the trade lost money.

## 3. Pay a supplier with maker-checker

1. As the **treasurer**, open **Payments**, fill in the form and press **Screen**. A large payment to an unknown beneficiary
   is flagged with reasons; a routine supplier is not.
2. **Create draft**, then **Submit**. Amounts above the threshold (or flagged ones) need two approvals.
3. Sign out and sign in as **approver@ballast.example**. The pending payment shows **Approve** and **Reject** (rejection
   asks for a reason). For a two-approval payment, a second approver (`approver2@ballast.example`) must also approve.
4. Back as the treasurer, **Release**. The releaser cannot be one of the approvers.
5. Open **Audit log** to see every step, and **Verify chain** to check integrity.

## 4. Net intercompany balances

**In-house bank** previews gross versus net volume for a settlement currency. A treasurer can **Run netting cycle**: the
balances are cleared and the cycle is recorded. A second run reports there is nothing to net.

## 5. Approve from a phone

The Blazor app signs in with the same users. **Approvals** lists pending payments with their flags and shows
Approve/Reject only to users who are allowed to act. **Alerts** lists limit breaches, covenants and AI flags.

## 6. Explore the AI layer

**AI models** shows the model catalog, the forecast quality measured on the seeded history, the regime parameters per
currency, and flagged items. `code/ai_models/examples` runs the same models standalone:

```bash
dotnet run --project code/ai_models/examples -c Release -- forecast   # also: anomaly, regimes, hedging, all
```
