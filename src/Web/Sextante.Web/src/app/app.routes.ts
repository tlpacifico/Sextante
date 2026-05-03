import { Routes } from '@angular/router';
import { AuthShellComponent } from './layout/auth-shell.component';
import { AppShellComponent } from './layout/app-shell.component';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    component: AuthShellComponent,
    children: [
      {
        path: 'login',
        loadComponent: () =>
          import('./auth/pages/login.page').then((m) => m.LoginPage),
        title: 'Sextante — Iniciar sessão',
      },
      {
        path: 'signup',
        loadComponent: () =>
          import('./auth/pages/signup.page').then((m) => m.SignupPage),
        title: 'Sextante — Criar conta',
      },
      {
        path: 'forgot-password',
        loadComponent: () =>
          import('./auth/pages/forgot-password.page').then(
            (m) => m.ForgotPasswordPage,
          ),
        title: 'Sextante — Recuperar palavra-passe',
      },
      {
        path: 'reset-password',
        loadComponent: () =>
          import('./auth/pages/reset-password.page').then(
            (m) => m.ResetPasswordPage,
          ),
        title: 'Sextante — Definir nova palavra-passe',
      },
      { path: '', redirectTo: 'login', pathMatch: 'full' },
    ],
  },
  {
    path: 'app',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.page').then(
            (m) => m.DashboardPage,
          ),
        title: 'Sextante — Dashboard',
      },
      {
        path: 'accounts',
        loadComponent: () =>
          import('./features/financial/pages/accounts.page').then(
            (m) => m.AccountsPage,
          ),
        title: 'Sextante — Contas',
      },
      {
        path: 'categories',
        loadComponent: () =>
          import('./features/financial/pages/categories.page').then(
            (m) => m.CategoriesPage,
          ),
        title: 'Sextante — Categorias',
      },
      {
        path: 'transactions',
        loadComponent: () =>
          import('./features/financial/pages/transactions/transactions.page').then(
            (m) => m.TransactionsPage,
          ),
        title: 'Sextante — Transações',
      },
      {
        path: 'settings/general',
        loadComponent: () =>
          import('./features/settings/settings-general.page').then(
            (m) => m.SettingsGeneralPage,
          ),
        title: 'Sextante — Definições',
      },
      {
        path: 'admin/currencies',
        loadComponent: () =>
          import('./features/admin/currencies.page').then(
            (m) => m.AdminCurrenciesPage,
          ),
        title: 'Sextante — Moedas',
      },
      {
        path: 'admin/exchange-rates',
        loadComponent: () =>
          import('./features/admin/exchange-rates.page').then(
            (m) => m.AdminExchangeRatesPage,
          ),
        title: 'Sextante — Taxas de câmbio',
      },
      {
        path: 'categorization-rules',
        loadComponent: () =>
          import('./features/financial/pages/categorization-rules.page').then(
            (m) => m.CategorizationRulesPage,
          ),
        title: 'Sextante — Regras de categorização',
      },
      {
        path: 'import-profiles',
        loadComponent: () =>
          import('./features/financial/pages/import-profiles.page').then(
            (m) => m.ImportProfilesPage,
          ),
        title: 'Sextante — Perfis de importação',
      },
      {
        path: 'imports/new',
        loadComponent: () =>
          import('./features/financial/pages/import-wizard.page').then(
            (m) => m.ImportWizardPage,
          ),
        title: 'Sextante — Nova importação',
      },
      {
        path: 'imports',
        loadComponent: () =>
          import('./features/financial/pages/import-batches.page').then(
            (m) => m.ImportBatchesPage,
          ),
        title: 'Sextante — Importações',
      },
      {
        path: 'recurrings',
        loadComponent: () =>
          import('./features/financial/pages/recurring-rules.page').then(
            (m) => m.RecurringRulesPage,
          ),
        title: 'Sextante — Recorrentes',
      },
      {
        path: 'budgets',
        loadComponent: () =>
          import('./features/financial/pages/budgets.page').then(
            (m) => m.BudgetsPage,
          ),
        title: 'Sextante — Orçamentos',
      },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
    ],
  },
  { path: '**', redirectTo: '/login' },
];
