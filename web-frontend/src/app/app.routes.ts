import { Routes } from '@angular/router';
import { Shell } from './components/shell';
import { authGuard, guestGuard } from './services/guards';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in | Ballast',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard | Ballast',
        loadComponent: () =>
          import('./pages/dashboard').then((m) => m.DashboardPage),
      },
      {
        path: 'cash',
        title: 'Cash positions | Ballast',
        loadComponent: () => import('./pages/cash').then((m) => m.CashPage),
      },
      {
        path: 'forecast',
        title: 'Forecast | Ballast',
        loadComponent: () =>
          import('./pages/forecast').then((m) => m.ForecastPage),
      },
      {
        path: 'fx',
        title: 'FX and hedging | Ballast',
        loadComponent: () => import('./pages/fx').then((m) => m.FxPage),
      },
      {
        path: 'risk',
        title: 'Risk | Ballast',
        loadComponent: () => import('./pages/risk').then((m) => m.RiskPage),
      },
      {
        path: 'inhouse-bank',
        title: 'In-house bank | Ballast',
        loadComponent: () =>
          import('./pages/inhouse-bank').then((m) => m.InHouseBankPage),
      },
      {
        path: 'debt',
        title: 'Debt and covenants | Ballast',
        loadComponent: () => import('./pages/debt').then((m) => m.DebtPage),
      },
      {
        path: 'payments',
        title: 'Payments | Ballast',
        loadComponent: () =>
          import('./pages/payments').then((m) => m.PaymentsPage),
      },
      {
        path: 'ai',
        title: 'AI models | Ballast',
        loadComponent: () =>
          import('./pages/ai-models').then((m) => m.AiModelsPage),
      },
      {
        path: 'audit',
        title: 'Audit log | Ballast',
        loadComponent: () => import('./pages/audit').then((m) => m.AuditPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
