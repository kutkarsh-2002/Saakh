import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthApi } from './core/api/auth.api';
import { SaakhApi } from './core/api/saakh.api';
import { SessionStore } from './core/auth/session.store';
import { AppNotification, AvailabilityStatus, VerificationStatus } from './core/models/domain';
import { ROLE_META } from './core/models/status-vocabulary';
import { RealtimeService } from './core/realtime/realtime.service';
import { formatRelative } from './core/util/format';
import { ToastService } from './core/util/toast.service';
import { StatusPill } from './shared/status-pill';
import { ConfirmDialog, ConfirmDialogData, ConfirmDialogResult } from './shared/confirm-dialog';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  /** False while the account is unverified, which greys the tab and explains why. */
  requiresVerification: boolean;
}

const TRADER_NAV: NavItem[] = [
  { path: '/dashboard', label: 'Opportunity', icon: 'explore', requiresVerification: true },
  { path: '/interests', label: 'Interests', icon: 'handshake', requiresVerification: true },
  { path: '/deals', label: 'Deals', icon: 'receipt_long', requiresVerification: true },
  { path: '/history', label: 'History', icon: 'history', requiresVerification: true },
  { path: '/profile', label: 'Profile', icon: 'person', requiresVerification: false },
];

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatMenuModule,
    MatTooltipModule,
    StatusPill,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly store = inject(SessionStore);
  private readonly auth = inject(AuthApi);
  private readonly api = inject(SaakhApi);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);

  readonly VerificationStatus = VerificationStatus;
  readonly roleMeta = ROLE_META;
  readonly formatRelative = formatRelative;

  readonly session = this.store.session;
  readonly profile = this.store.profile;
  readonly admin = this.store.admin;
  readonly isAdmin = this.store.isAdmin;
  readonly isTrader = this.store.isTrader;
  readonly featuresUnlocked = this.store.featuresUnlocked;
  readonly displayName = this.store.displayName;
  readonly realtimeConnected = this.realtime.connected;

  readonly notifications = signal<AppNotification[]>([]);
  readonly unreadCount = computed(() => this.notifications().filter((n) => !n.isRead).length);

  readonly nav = computed(() => (this.isTrader() ? TRADER_NAV : []));

  readonly availabilityLabel = computed(() =>
    this.profile()?.availabilityStatus === AvailabilityStatus.Active ? 'Active' : 'Inactive',
  );

  readonly canToggleAvailability = computed(() => {
    const status = this.profile()?.availabilityStatus;
    // An admin suspension is not something the user can toggle their way out of.
    return status === AvailabilityStatus.Active || status === AvailabilityStatus.Inactive;
  });

  constructor() {
    // The live layer follows the session: it starts once signed in and stops on
    // sign-out, so a shared device never leaks one vendor's chat to the next.
    effect(() => {
      if (this.session()) {
        void this.realtime.start();
        this.loadNotifications();
      } else {
        void this.realtime.stop();
        this.notifications.set([]);
      }
    });

    this.realtime.notifications$.subscribe((notification) => {
      this.notifications.update((list) => [notification, ...list].slice(0, 100));
    });

    // A verification decision arriving over the socket has to re-read the
    // session, because it changes which tabs this account can reach.
    this.realtime.verification$.subscribe(() => {
      this.auth.session().subscribe({ next: () => undefined, error: () => undefined });
    });
  }

  private loadNotifications(): void {
    if (!this.store.isTrader()) {
      return;
    }

    this.api.notifications().subscribe({
      next: (list) => this.notifications.set(list),
      error: () => undefined,
    });
  }

  openNotification(notification: AppNotification): void {
    this.api.markNotificationsRead(notification.id).subscribe({
      next: () => {
        this.notifications.update((list) =>
          list.map((n) => (n.id === notification.id ? { ...n, isRead: true } : n)),
        );
      },
      error: () => undefined,
    });

    if (notification.link) {
      void this.router.navigateByUrl(notification.link);
    }
  }

  markAllRead(): void {
    this.api.markNotificationsRead().subscribe({
      next: () =>
        this.notifications.update((list) => list.map((n) => ({ ...n, isRead: true }))),
      error: (error: unknown) => this.toast.error(error),
    });
  }

  toggleAvailability(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    const goingInactive = profile.availabilityStatus === AvailabilityStatus.Active;

    const data: ConfirmDialogData = goingInactive
      ? {
          title: 'Go inactive?',
          message:
            'Your profile leaves search straight away and stops receiving new interest requests.',
          consequence:
            'Your in-flight deals carry on exactly as they are. You can switch back to Active whenever you want.',
          confirmLabel: 'Go inactive',
        }
      : {
          title: 'Go active?',
          message: 'Your profile returns to search and can receive new interest requests again.',
          confirmLabel: 'Go active',
        };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((result) => {
        if (!result?.confirmed) {
          return;
        }

        this.api.setAvailability(!goingInactive).subscribe({
          next: (updated) => {
            const session = this.session();
            if (session) {
              this.store.setSession({ ...session, profile: updated });
            }
            this.toast.success(
              goingInactive
                ? 'You are now inactive and hidden from search.'
                : 'You are active and visible in search again.',
            );
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }

  signOut(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/sign-in']),
      error: () => void this.router.navigate(['/sign-in']),
    });
  }
}
