import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { SessionStore } from '../../core/auth/session.store';
import { Interest, InterestStatus } from '../../core/models/domain';
import { CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { formatRelative } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { ConfirmDialog, ConfirmDialogData, ConfirmDialogResult } from '../../shared/confirm-dialog';
import { EmptyState, LoadingBlock, PageHeader } from '../../shared/ui';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';

type Box = 'received' | 'sent';

/**
 * The interest inbox: one-sided signals in both directions.
 *
 * Received comes first and is the default tab, because an unanswered inbound
 * interest is the only thing here that someone else is waiting on.
 */
@Component({
  selector: 'sk-interests-page',
  imports: [
    RouterLink,
    PageHeader,
    EmptyState,
    LoadingBlock,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './interests.page.html',
  styleUrl: './interests.page.scss',
})
export class InterestsPage {
  private readonly api = inject(SaakhApi);
  private readonly store = inject(SessionStore);
  private readonly realtime = inject(RealtimeService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly InterestStatus = InterestStatus;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly formatRelative = formatRelative;

  readonly loading = signal(true);
  readonly all = signal<Interest[]>([]);
  readonly box = signal<Box>('received');

  readonly counterpartyLabel = computed(() => {
    const role = this.store.profile()?.role;
    return role === undefined ? 'counterparties' : ROLE_META[role].discovers.toLowerCase();
  });

  readonly received = computed(() => this.all().filter((item) => !item.sentByMe));
  readonly sent = computed(() => this.all().filter((item) => item.sentByMe));

  readonly awaitingMe = computed(
    () => this.received().filter((item) => item.status === InterestStatus.Sent).length,
  );

  readonly rows = computed(() => (this.box() === 'received' ? this.received() : this.sent()));

  constructor() {
    this.load();

    // An interest answered on the other side should land here without a refresh.
    this.realtime.interest$.subscribe(() => this.load());
  }

  private load(): void {
    this.loading.set(true);
    this.api.interests('all').subscribe({
      next: (rows) => {
        this.all.set(rows);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
      },
    });
  }

  accept(interest: Interest): void {
    this.respond(interest, true);
  }

  decline(interest: Interest): void {
    const data: ConfirmDialogData = {
      title: `Decline ${interest.counterparty.name}?`,
      message: 'They will be told you declined. No chat opens and no deal is created.',
      consequence:
        'Declining is recorded against neither profile and does not affect anyone’s rating. They can send interest again later.',
      confirmLabel: 'Decline',
      cancelLabel: 'Keep it open',
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '520px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result?.confirmed) {
          this.respond(interest, false);
        }
      });
  }

  private respond(interest: Interest, accept: boolean): void {
    this.api.respondToInterest(interest.id, accept).subscribe({
      next: (updated) => {
        this.all.update((rows) => rows.map((row) => (row.id === updated.id ? updated : row)));

        if (accept) {
          this.toast.success(`Chat is open with ${interest.counterparty.name}.`);
          void this.router.navigate(['/interests', interest.id]);
        } else {
          this.toast.info(`You declined ${interest.counterparty.name}.`);
        }
      },
      error: (error: unknown) => this.toast.error(error),
    });
  }
}
