using Ballast.Mobile.UI.Models;
using Ballast.Mobile.UI.Services;
using Ballast.Mobile.UI.Utils;
using Ballast.TestKit;

namespace Ballast.Mobile.Tests;

public class FormattersTests
{
    [Fact]
    public void Money_UsesSymbolsGroupingAndSign()
    {
        Assert.Equal("$1,234,567", Formatters.Money(1234567m, "USD"));
        Assert.Equal("-$1,234.50", Formatters.Money(-1234.5m, "USD", 2));
        Assert.Equal("\u20AC10.00", Formatters.Money(10m, "EUR", 2));
        Assert.Equal("CHF 5", Formatters.Money(5m, "CHF"));
        Assert.Equal("XYZ 7", Formatters.Money(7m, "XYZ"));
    }

    [Fact]
    public void Compact_PctAndHumanize()
    {
        Assert.Equal("1.5M", Formatters.Compact(1_500_000));
        Assert.Equal("-2K", Formatters.Compact(-2_000));
        Assert.Equal("12.3%", Formatters.Pct(0.1234, 1));
        Assert.Equal("Pending Approval", Formatters.Humanize("PendingApproval"));
        Assert.Equal("warn", Formatters.Tone("PendingApproval"));
        Assert.Equal("bad", Formatters.Tone("Breach"));
        Assert.Equal("neutral", Formatters.Tone("x"));
    }
}

public class ChartGeometryTests
{
    [Fact]
    public void Build_ReturnsNullForNoPoints()
    {
        Assert.True(ChartGeometry.Build([], 360, 190) is null);
    }

    [Fact]
    public void Build_ProducesLineBandTicksAndZeroLine()
    {
        var pts = new List<ChartPoint> { new("a", 100, 50, 150), new("b", 120, 60, 190), new("c", 90, -20, 160), new("d", 95, -30, 170) };
        var g = ChartGeometry.Build(pts, 360, 190)!;
        Assert.True(g.LinePath.StartsWith('M'));
        Assert.True(g.BandPath.EndsWith('Z'));
        Assert.True(g.YTicks.Count >= 3);
        Assert.True(g.ZeroY.HasValue, "range crosses zero");
        Assert.True(g.XTicks.Count >= 1);
    }

    [Fact]
    public void Build_WithoutIntervalsHasNoBandAndHandlesFlatSeries()
    {
        var g = ChartGeometry.Build([new("a", 5), new("b", 5)], 360, 190)!;
        Assert.Equal("", g.BandPath);
        Assert.True(g.YTicks.Count >= 2);
    }

    [Fact]
    public void NiceTicks_CoverTheRangeAscending()
    {
        var t = ChartGeometry.NiceTicks(-130, 870, 5);
        Assert.True(t[0] <= -130 && t[^1] >= 870);
        for (int i = 1; i < t.Count; i++) Assert.True(t[i] > t[i - 1]);
        Assert.Equal(1, ChartGeometry.NiceTicks(3, 3).Count);
    }
}

public class ApprovalRulesTests
{
    private static readonly UserInfo Approver = new("U-3", "a@x.com", "Approver", "Approver");
    private static readonly UserInfo Admin = new("U-1", "ad@x.com", "Admin", "Admin");
    private static readonly UserInfo Treasurer = new("U-2", "t@x.com", "Treasurer", "Treasurer");

    private static Payment P(string status = "PendingApproval", string createdBy = "U-2", params string[] approvedBy) =>
        new("P", "A", "B", 1m, "USD", "", new DateOnly(2026, 11, 9), status, createdBy, DateTimeOffset.UtcNow,
            approvedBy.Select(u => new PaymentApproval(u, u, DateTimeOffset.UtcNow)).ToList(), [], 2, null);

    [Fact]
    public void OnlyCheckersCanApproveRejectPendingPayments()
    {
        Assert.True(ApprovalRules.CanApprove(P(), Approver));
        Assert.True(ApprovalRules.CanApprove(P(), Admin));
        Assert.False(ApprovalRules.CanApprove(P(), Treasurer));
        Assert.False(ApprovalRules.CanApprove(P(), null));
        Assert.False(ApprovalRules.CanApprove(P("Approved"), Approver));
        Assert.True(ApprovalRules.CanReject(P(), Approver));
        Assert.False(ApprovalRules.CanReject(P(), Treasurer));
    }

    [Fact]
    public void MakerCheckerAndNoDoubleApproval()
    {
        Assert.False(ApprovalRules.CanApprove(P(createdBy: "U-3"), Approver), "creator cannot approve");
        Assert.False(ApprovalRules.CanApprove(P("PendingApproval", "U-2", "U-3"), Approver), "already approved");
        Assert.True(ApprovalRules.CanReject(P("PendingApproval", "U-2", "U-3"), Approver));
    }
}
