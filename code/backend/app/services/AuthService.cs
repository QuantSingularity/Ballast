using System.Text.RegularExpressions;
using Ballast.Api.Schemas;
using Ballast.Core;
using Ballast.Core.Domain;
using Ballast.Data;
using Ballast.Infrastructure;
using Ballast.Treasury;

namespace Ballast.Api.Services;

public sealed partial class AuthService(
    BallastDb db, PasswordHasher hasher, JwtTokenService jwt, AuditLog audit, BallastSettings settings, TimeProvider clock)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, (int Failures, DateTimeOffset? LockedUntil)> _attempts = [];
    private readonly object _lock = new();
    private readonly string _dummyHash = hasher.Hash("not-a-real-password");

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static UserDto ToDto(User u) => new(u.Id, u.Email, u.Name, u.Role);

    private AuthResponse Issue(User user) => new(ToDto(user),
        jwt.Issue(user, JwtTokenService.AccessType, TimeSpan.FromMinutes(settings.AccessTokenMinutes)),
        jwt.Issue(user, JwtTokenService.RefreshType, TimeSpan.FromDays(settings.RefreshTokenDays)),
        settings.AccessTokenMinutes * 60);

    private User? FindByEmail(string email) =>
        db.Users.All().FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));

    public AuthResponse Register(RegisterRequest req)
    {
        if (db.Users.Count > 0 && !settings.AllowSelfRegistration)
            throw new ForbiddenException("Self-registration is disabled. Ask an administrator to create your account");
        var email = (req.Email ?? "").Trim();
        var name = (req.Name ?? "").Trim();
        if (!EmailPattern().IsMatch(email)) throw new ValidationException("A valid email address is required");
        if (name.Length < 2) throw new ValidationException("Name must be at least 2 characters");
        PasswordPolicy.Validate(req.Password);
        User user;
        lock (_lock)
        {
            if (FindByEmail(email) is not null) throw new ConflictException("An account with this email already exists");
            var role = db.Users.Count == 0 ? UserRole.Admin : UserRole.Analyst;
            user = new User($"U-{db.Users.Count + 1:D3}", email.ToLowerInvariant(), name, role, hasher.Hash(req.Password!), clock.GetUtcNow());
            db.Users.Upsert(user);
        }
        audit.Append(user.Email, "auth.register", user.Id, $"Registered with role {user.Role}");
        db.SaveChanges();
        return Issue(user);
    }

    public AuthResponse Login(LoginRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        lock (_lock)
        {
            if (_attempts.TryGetValue(email, out var a) && a.LockedUntil is { } until && until > clock.GetUtcNow())
                throw new LockedException("Too many failed attempts. Try again later");
        }
        var user = FindByEmail(email);
        bool ok = hasher.Verify(req.Password ?? "", user?.PasswordHash ?? _dummyHash) && user is not null;
        lock (_lock)
        {
            if (!ok)
            {
                var failures = _attempts.TryGetValue(email, out var a) ? a.Failures + 1 : 1;
                _attempts[email] = (failures, failures >= MaxFailures ? clock.GetUtcNow().Add(LockDuration) : null);
            }
            else _attempts.Remove(email);
        }
        if (!ok)
        {
            audit.Append(email, "auth.login_failed", user?.Id ?? "unknown", "Invalid credentials");
            throw new UnauthorizedException("Invalid email or password");
        }
        audit.Append(user!.Email, "auth.login", user.Id, "Signed in");
        return Issue(user);
    }

    public AuthResponse Refresh(RefreshRequest req)
    {
        var claims = jwt.Validate(req.RefreshToken ?? "", JwtTokenService.RefreshType)
            ?? throw new UnauthorizedException("Invalid or expired refresh token");
        var user = db.Users.Find(claims.Subject) ?? throw new UnauthorizedException("Account no longer exists");
        return Issue(user);
    }

    public UserDto CreateUser(CreateUserRequest req, Actor actor)
    {
        var email = (req.Email ?? "").Trim();
        var name = (req.Name ?? "").Trim();
        if (!EmailPattern().IsMatch(email)) throw new ValidationException("A valid email address is required");
        if (name.Length < 2) throw new ValidationException("Name must be at least 2 characters");
        if (!Enum.IsDefined(req.Role)) throw new ValidationException("Unknown role");
        PasswordPolicy.Validate(req.Password);
        User user;
        lock (_lock)
        {
            if (FindByEmail(email) is not null) throw new ConflictException("An account with this email already exists");
            user = new User($"U-{db.Users.Count + 1:D3}", email.ToLowerInvariant(), name, req.Role, hasher.Hash(req.Password!), clock.GetUtcNow());
            db.Users.Upsert(user);
        }
        audit.Append(actor.Name, "auth.user_create", user.Id, $"Created {user.Email} with role {user.Role}");
        db.SaveChanges();
        return ToDto(user);
    }

    public UserDto Profile(string userId) => ToDto(db.Users.Get(userId));

    public UserDto ChangeRole(string targetId, UserRole role, Actor actor)
    {
        var user = db.Users.Get(targetId);
        if (user.Id == actor.Id) throw new ForbiddenException("Administrators cannot change their own role");
        var updated = user with { Role = role };
        db.Users.Upsert(updated);
        audit.Append(actor.Name, "auth.role_change", user.Id, $"{user.Role} to {role}");
        db.SaveChanges();
        return ToDto(updated);
    }

    public IReadOnlyList<UserDto> Users() => db.Users.All().Select(ToDto).ToList();
}
