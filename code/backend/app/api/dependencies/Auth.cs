using System.Security.Claims;
using System.Text.Encodings.Web;
using Ballast.Api.Schemas;
using Ballast.Core.Domain;
using Ballast.Infrastructure;
using Ballast.Treasury;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ballast.Api.Dependencies;

public sealed class BearerAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, JwtTokenService jwt)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Bearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = jwt.Validate(header[7..].Trim(), JwtTokenService.AccessType);
        if (claims is null) return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token"));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, claims.Subject),
            new Claim(ClaimTypes.Email, claims.Email),
            new Claim(ClaimTypes.Name, claims.Name),
            new Claim(ClaimTypes.Role, claims.Role.ToString())
        ], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ErrorBody("unauthorized", "Authentication is required"));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new ErrorBody("forbidden", "You do not have permission to perform this action"));
    }
}

public static class CurrentUser
{
    public static Actor ToActor(this ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
        user.FindFirstValue(ClaimTypes.Name) ?? "",
        Enum.Parse<UserRole>(user.FindFirstValue(ClaimTypes.Role) ?? nameof(UserRole.Analyst)));
}

public static class Policies
{
    public const string Maker = "Maker";
    public const string Checker = "Checker";
    public const string AdminOnly = "AdminOnly";
}
