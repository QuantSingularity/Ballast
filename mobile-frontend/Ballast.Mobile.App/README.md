# Ballast.Mobile.App

Thin .NET MAUI Blazor Hybrid host for the shared `Ballast.Mobile.UI` Razor class library.

All screens, API access, token refresh and approval rules live in `Ballast.Mobile.UI`, which is built and tested
(`../Ballast.Mobile.Tests`). This project only supplies:

- `SecureTokenStore`: keeps the session in the platform keystore (Keychain on iOS, Keystore on Android).
- `ApiAddress`: debug builds use `10.0.2.2:5000` on the Android emulator and `localhost:5000` on iOS. Release builds
  must use HTTPS; edit `ProductionAddress` or set the `ballast.api_base_address` preference.
- Platform entry points, icons and a network security config that allows cleartext only to `10.0.2.2` and `localhost`.

## Verification status

This host was written without access to the MAUI workloads and was **not compiled or run** where the rest of the
repository was verified. Build it on a machine with the MAUI workload installed:

```bash
dotnet workload install maui-android      # or maui-ios on macOS
dotnet build -f net10.0-android
```

If a version or template detail has drifted, the fastest fix is to generate a fresh `dotnet new maui-blazor`
project and copy `MauiProgram.cs`, `SecureTokenStore.cs`, `ApiAddress.cs` and the `ProjectReference` line
(`AdditionalProperties="BallastUsePackages=true"`) into it.
