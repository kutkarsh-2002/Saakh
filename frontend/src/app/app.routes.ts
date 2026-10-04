import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard, traderGuard, verifiedGuard } from './core/auth/guards';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard',
  },

  // ---- public -------------------------------------------------------------
  {
    path: 'sign-in',
    canActivate: [guestGuard],
    title: 'Sign in · Saakh',
    loadComponent: () => import('./features/auth/sign-in.page').then((m) => m.SignInPage),
  },
  {
    path: 'sign-up',
    canActivate: [guestGuard],
    title: 'Create your profile · Saakh',
    loadComponent: () => import('./features/auth/sign-up.page').then((m) => m.SignUpPage),
  },

  // ---- Lender / Seeker ----------------------------------------------------
  // Profile is reachable on the authenticated+trader guards alone: it is the
  // one tab a Needs Approval, Pending or Rejected account can still open.
  {
    path: 'profile',
    canActivate: [traderGuard],
    title: 'Profile & verification · Saakh',
    loadComponent: () => import('./features/profile/profile.page').then((m) => m.ProfilePage),
  },

  // Everything below also needs a verified profile.
  {
    path: 'dashboard',
    canActivate: [traderGuard, verifiedGuard],
    title: 'Opportunity · Saakh',
    loadComponent: () =>
      import('./features/dashboard/dashboard.page').then((m) => m.DashboardPage),
  },
  {
    path: 'interests',
    canActivate: [traderGuard, verifiedGuard],
    title: 'Interests · Saakh',
    loadComponent: () => import('./features/interests/interests.page').then((m) => m.InterestsPage),
  },
  {
    path: 'interests/:id',
    canActivate: [traderGuard, verifiedGuard],
    title: 'Negotiation · Saakh',
    loadComponent: () =>
      import('./features/interests/negotiation.page').then((m) => m.NegotiationPage),
  },
  {
    path: 'deals',
    canActivate: [traderGuard, verifiedGuard],
    title: 'Open deals · Saakh',
    loadComponent: () => import('./features/deals/open-deals.page').then((m) => m.OpenDealsPage),
  },
  {
    path: 'deals/:id',
    canActivate: [traderGuard, verifiedGuard],
    title: 'Deal workspace · Saakh',
    loadComponent: () =>
      import('./features/deals/deal-workspace.page').then((m) => m.DealWorkspacePage),
  },
  {
    path: 'history',
    canActivate: [traderGuard, verifiedGuard],
    title: 'History · Saakh',
    loadComponent: () => import('./features/deals/history.page').then((m) => m.HistoryPage),
  },
  {
    path: 'counterparties/:id',
    canActivate: [traderGuard, verifiedGuard],
    title: 'Trust record · Saakh',
    loadComponent: () =>
      import('./features/dashboard/counterparty.page').then((m) => m.CounterpartyPage),
  },

  // ---- Admin --------------------------------------------------------------
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/admin/admin.shell').then((m) => m.AdminShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'queue' },
      {
        path: 'queue',
        title: 'Verification queue · Saakh Admin',
        loadComponent: () =>
          import('./features/admin/verification-queue.page').then((m) => m.VerificationQueuePage),
      },
      {
        path: 'directory',
        title: 'User directory · Saakh Admin',
        loadComponent: () => import('./features/admin/directory.page').then((m) => m.DirectoryPage),
      },
      {
        path: 'directory/:id',
        title: 'Profile · Saakh Admin',
        loadComponent: () =>
          import('./features/admin/admin-profile.page').then((m) => m.AdminProfilePage),
      },
      {
        path: 'activity',
        title: 'Action log · Saakh Admin',
        loadComponent: () =>
          import('./features/admin/action-log.page').then((m) => m.ActionLogPage),
      },
    ],
  },

  {
    path: 'not-found',
    canActivate: [authGuard],
    title: 'Not found · Saakh',
    loadComponent: () => import('./features/not-found.page').then((m) => m.NotFoundPage),
  },
  { path: '**', redirectTo: 'not-found' },
];
