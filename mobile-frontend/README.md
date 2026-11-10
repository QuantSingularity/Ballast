# Mobile frontend

Blazor, split so that everything with logic can be built and tested without MAUI workloads.

| Project                | Contents                                                                                                                                             | Status                |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------- |
| `Ballast.Mobile.UI`    | Razor class library: pages, components, API client with token refresh, approval view model, formatters, chart geometry                               | Built and tested      |
| `Ballast.Mobile.Tests` | Runs the real API in process (with the Python model service) and tests the client, view models and every page (rendered to HTML with `HtmlRenderer`) | 30 tests passing      |
| `Ballast.Mobile.App`   | .NET MAUI Blazor Hybrid host (secure token store, API address, platform files)                                                                       | Written, not compiled |

```bash
dotnet run --project Ballast.Mobile.Tests -c Release
```

Screens: Home (cash and attention items), Forecast, Approvals (approve or reject), Alerts, FX, Settings. The host project
README explains how to build it and what to do if a template detail has drifted.
