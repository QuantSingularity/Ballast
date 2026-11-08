using Ballast.Mobile.UI.Components;
using Ballast.Mobile.UI.Models;
using Ballast.Mobile.UI.Pages;
using Ballast.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Mobile.Tests;

public class PageRenderTests
{
    [Fact]
    public async Task LoginPage_RendersFormWithDisabledSubmit()
    {
        var sp = Shared.Services(await Shared.Server);
        var html = await Shared.RenderAsync<Login>(sp);
        Assert.Contains("type=\"email\"", html);
        Assert.Contains("type=\"password\"", html);
        Assert.Contains("Sign in", html);
        Assert.Contains("disabled", html);
    }

    [Fact]
    public async Task HomePage_ShowsCashKpisAndAttentionItems()
    {
        var (sp, _, nav) = await Shared.SignedInAsync(Demo.Approver);
        var html = await Shared.RenderAsync<Home>(sp);
        Assert.Contains("Total cash", html);
        Assert.Contains("$38,417,400", html);
        Assert.Contains("Liquidity coverage", html);
        Assert.Contains("Needs attention", html);
        Assert.Contains("Minimum hedge ratio", html);
        Assert.Contains("By currency", html);
        Assert.Equal(0, nav.Navigations.Count);
    }

    [Fact]
    public async Task HomePage_RedirectsToLoginWhenSignedOut()
    {
        var nav = new TestNavigationManager();
        var sp = Shared.Services(await Shared.Server, nav);
        var html = await Shared.RenderAsync<Home>(sp);
        Assert.True(nav.Navigations.Contains("login"), string.Join(",", nav.Navigations));
        Assert.False(html.Contains("alert bad"), "a redirect is not an error banner");
    }

    [Fact]
    public async Task ApprovalsPage_ShowsActionsOnlyToCheckers()
    {
        var server = await Shared.Server;
        string id = await server.CreatePendingPaymentAsync(3_579m, "Stark Freight");

        var (approverSp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var approverHtml = await Shared.RenderAsync<Approvals>(approverSp);
        Assert.Contains(id, approverHtml);
        Assert.Contains(">Approve<", approverHtml);
        Assert.Contains(">Reject<", approverHtml);
        Assert.Contains("Meridian Advisory Partners", approverHtml);
        Assert.Contains("no payment history", approverHtml);

        var (treasurerSp, _, _) = await Shared.SignedInAsync(Demo.Treasurer);
        var treasurerHtml = await Shared.RenderAsync<Approvals>(treasurerSp);
        Assert.Contains(id, treasurerHtml);
        Assert.False(treasurerHtml.Contains(">Approve<"), "makers get no approve button");
        Assert.False(treasurerHtml.Contains(">Reject<"));
    }

    [Fact]
    public async Task ForecastPage_RendersChartAndHorizonControls()
    {
        var (sp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var html = await Shared.RenderAsync<ForecastPage>(sp);
        Assert.Contains("<svg", html);
        Assert.Contains("class=\"line\"", html);
        Assert.Contains("class=\"band\"", html);
        Assert.Contains("30 days", html);
        Assert.Contains("91 days", html);
        Assert.Contains("Expected net flow", html);
        Assert.Contains("rolling origins", html);
    }

    [Fact]
    public async Task AlertsPage_ListsLimitsCovenantsAndAiFlags()
    {
        var (sp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var html = await Shared.RenderAsync<Alerts>(sp);
        Assert.Contains("Minimum hedge ratio", html);
        Assert.Contains("Minimum interest cover", html);
        Assert.Contains("Warning", html);
        Assert.Contains("Meridian Advisory Partners", html);
    }

    [Fact]
    public async Task FxPage_ShowsExposureAndRecommendations()
    {
        var (sp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var html = await Shared.RenderAsync<Fx>(sp);
        Assert.Contains("Next 30 days", html);
        Assert.Contains("EUR", html);
        Assert.Contains("Hedge recommendations", html);
    }

    [Fact]
    public async Task SettingsPage_ShowsProfileAndServer()
    {
        var (sp, _, _) = await Shared.SignedInAsync(Demo.Approver);
        var html = await Shared.RenderAsync<Settings>(sp);
        Assert.Contains("Alex Approver", html);
        Assert.Contains(Demo.Approver, html);
        Assert.Contains("http://127.0.0.1:", html);
        Assert.Contains("Sign out", html);
    }

    [Fact]
    public async Task SparkChart_EncodesLabelsAndHandlesEmptyData()
    {
        var sp = Shared.Services(await Shared.Server);
        var html = await Shared.RenderAsync<SparkChart>(sp, new()
        {
            ["Points"] = (IReadOnlyList<ChartPoint>)[new("<b>x</b>", 1, 0, 2), new("y", 2, 1, 3)]
        });
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
        Assert.False(html.Contains("<b>x</b>"), "labels must be HTML encoded");
        var empty = await Shared.RenderAsync<SparkChart>(sp);
        Assert.Contains("No data to chart", empty);
    }

    [Fact]
    public async Task KpiTileAndStatusPill_RenderToneAndLabel()
    {
        var sp = Shared.Services(await Shared.Server);
        var tile = await Shared.RenderAsync<KpiTile>(sp, new() { ["Label"] = "Cash", ["Value"] = "$1", ["Tone"] = "good", ["Hint"] = "h" });
        Assert.Contains("tile good", tile);
        Assert.Contains("Cash", tile);
        var pill = await Shared.RenderAsync<StatusPill>(sp, new() { ["Status"] = "PendingApproval" });
        Assert.Contains("pill warn", pill);
        Assert.Contains("Pending Approval", pill);
    }
}
