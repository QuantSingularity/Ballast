# Web frontend

Angular 22 with standalone components, signals and zoneless change detection. No UI library: the charts are small SVG
components.

```bash
npm ci
npx ng serve          # http://localhost:4200, proxies /api to http://localhost:5000
npx ng test --watch=false
npm run build
```

Layout: `src/app/{components,pages,services,types,utils,testing}`. Pages are lazy loaded. Auth tokens are kept in
`sessionStorage` (see [docs/SECURITY.md](../docs/SECURITY.md) for the trade-off). Buttons for restricted actions are hidden by
role, but the API enforces the rules regardless.
