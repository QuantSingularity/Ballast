# Security

## What is implemented

**Passwords.** PBKDF2-SHA256, 16-byte random salt, 32-byte key, 210,000 iterations by default (minimum 100,000 in staging and
production). Verification uses a fixed-time comparison. Policy: at least 12 characters with upper case, lower case and a digit.

**Tokens.** HS256 JWTs signed with `BALLAST_SECRET_KEY` (at least 32 characters, enforced at startup). Each token carries a
`typ` claim, so a refresh token cannot be used as an access token or the reverse. Validation checks the algorithm (so
`alg: none` is rejected), compares signatures in fixed time and checks expiry. All of this is covered by tests, including a
tampered payload, a token signed with a different secret, and an expired token.

**Sign-in.** Failed sign-ins return the same message whether or not the account exists, and a dummy hash is verified for
unknown accounts to reduce timing differences. Five failures lock that account for five minutes. Auth endpoints are rate
limited per client IP (30 per minute by default).

**Authorization.** Role policies on endpoints, plus business rules in the workflow: a payment creator cannot approve it, an
approver cannot approve twice, the releaser must not be an approver, and larger or flagged payments need two distinct
approvers. These rules are unit tested and exercised through the API.

**Registration.** Open in development. Outside development the first account becomes Admin and after that only an Admin can
create users.

**Audit.** Authentication, payment, hedge, netting and role events are appended to a SHA-256 hash chain. Editing or deleting
an entry makes `GET /api/v1/audit/verify` fail (tested).

**Transport and browser hardening.** `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Cache-Control: no-store`
on API responses, restricted CORS origins, and a Content-Security-Policy in the nginx image. Terminate TLS at the ingress or
load balancer and add HSTS there.

**Fail-closed configuration.** The API refuses to start with a weak secret, with demo seeding plus the public demo password
in a production-like environment, or with low PBKDF2 iterations in one. A bug found during testing (production-like
settings built directly still seeded demo users) led to the environment-derived defaults now in place.

**Mobile.** Tokens are stored in the platform keystore through `SecureStorage`, Android backup is disabled, and cleartext HTTP
is permitted only for the emulator host and localhost (this part of the host is unverified, see its README).

## Known gaps

| Gap                                                        | Impact                                                                                   | What to do                                                                                                    |
| ---------------------------------------------------------- | ---------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| No multi-factor authentication                             | A stolen password is enough                                                              | Add TOTP or WebAuthn, or front the API with an identity provider                                              |
| Refresh tokens are stateless                               | They cannot be revoked and are not rotated; a leaked one works until it expires          | Store token ids server side, rotate on refresh                                                                |
| Role changes apply at the next token                       | A demoted user keeps the old role for up to 30 minutes                                   | Shorten lifetimes or check the role per request                                                               |
| Lockout state is in memory                                 | Resets on restart, not shared across instances, and can be used to lock someone else out | Move to a shared store and add per-IP throttling                                                              |
| Hash chain is tamper-evident, not tamper-proof             | Someone who can rewrite the whole snapshot can recompute the chain                       | Anchor periodic chain heads somewhere they cannot write (WORM storage, an external log)                       |
| Web tokens are in `sessionStorage`                         | Readable by script injected into the page                                                | The CSP and Angular's escaping reduce the risk; an HttpOnly cookie design would remove it                     |
| Hand-written JWT and hashing                               | Less scrutiny than a vetted package                                                      | Use `Microsoft.AspNetCore.Authentication.JwtBearer` and `PasswordHasher<T>` where a package feed is available |
| No password reset or email verification                    | Accounts are managed by Admins only                                                      | Add when an email channel exists                                                                              |
| Login failures record the attempted email in the audit log | Personal data in the log                                                                 | Hash or truncate if that is a concern                                                                         |
| Single-instance state store                                | No transactional guarantees                                                              | See the architecture document                                                                                 |
| No dependency or container scanning in CI                  | Known-vulnerable packages could slip in                                                  | Add `dotnet list package --vulnerable`, `npm audit`, and an image scanner                                     |

## Reporting a vulnerability

Open a private security advisory on the repository rather than a public issue.
