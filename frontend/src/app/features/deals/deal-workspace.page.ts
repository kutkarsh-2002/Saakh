import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SaakhApi } from '../../core/api/saakh.api';
import {
  DealCategory,
  DealDetail,
  DealState,
  Message,
  ProfileRole,
} from '../../core/models/domain';
import { CATEGORY_META, DEAL_STATE_STATUS, ROLE_META } from '../../core/models/status-vocabulary';
import { RealtimeService } from '../../core/realtime/realtime.service';
import {
  daysUntil,
  formatCapacity,
  formatDate,
  formatDateTime,
  formatRange,
} from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { ChatThread } from '../../shared/chat-thread';
import { DealTimeline } from '../../shared/deal-timeline';
import { LoadingBlock } from '../../shared/ui';
import { StarRating } from '../../shared/star-rating';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';
import { DealActions } from './deal-actions';

/**
 * The deal workspace: the ticket terms, the state timeline, the live chat and
 * the transitions this party may trigger, in one screen.
 *
 * The allowed transitions come from the API rather than being re-derived here,
 * so the UI can never offer an action the state machine will reject.
 */
@Component({
  selector: 'sk-deal-workspace-page',
  imports: [
    RouterLink,
    MatTooltipModule,
    ChatThread,
    DealTimeline,
    LoadingBlock,
    StarRating,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './deal-workspace.page.html',
  styleUrl: './deal-workspace.page.scss',
})
export class DealWorkspacePage {
  readonly id = input.required<string>();

  private readonly api = inject(SaakhApi);
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly actions = inject(DealActions);

  readonly DealState = DealState;
  readonly DealCategory = DealCategory;
  readonly ProfileRole = ProfileRole;
  readonly categoryMeta = CATEGORY_META;
  readonly stateMeta = DEAL_STATE_STATUS;
  readonly roleMeta = ROLE_META;
  readonly formatDate = formatDate;
  readonly formatDateTime = formatDateTime;

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly sending = signal(false);
  readonly detail = signal<DealDetail | null>(null);

  readonly deal = computed(() => this.detail()?.deal ?? null);

  readonly canStartProgress = computed(
    () => this.detail()?.allowedTransitions.includes(DealState.Progress) ?? false,
  );

  readonly canSettle = computed(
    () => this.detail()?.allowedTransitions.includes(DealState.Completed) ?? false,
  );

  readonly canHalt = computed(
    () => this.detail()?.allowedTransitions.includes(DealState.Halted) ?? false,
  );

  readonly amount = computed(() => {
    const deal = this.deal();
    return deal ? formatCapacity(deal.capacity, deal.capacityUnit, deal.category) : '';
  });

  readonly counterpartyCapacity = computed(() => {
    const party = this.deal()?.counterparty;
    return party
      ? formatRange(party.capacityMin, party.capacityMax, party.capacityUnit, party.category)
      : '';
  });

  readonly settlementNote = computed(() => {
    const deal = this.deal();
    if (!deal || deal.dealState === DealState.Completed || deal.dealState === DealState.Halted) {
      return null;
    }

    const days = daysUntil(deal.estimatedSettlementTime);
    if (days < 0) {
      return `${Math.abs(days)} ${Math.abs(days) === 1 ? 'day' : 'days'} past the estimate`;
    }
    if (days === 0) {
      return 'Due today';
    }
    return `${days} ${days === 1 ? 'day' : 'days'} to go`;
  });

  readonly overdue = computed(() => {
    const deal = this.deal();
    if (!deal || deal.dealState === DealState.Completed || deal.dealState === DealState.Halted) {
      return false;
    }
    return daysUntil(deal.estimatedSettlementTime) < 0;
  });

  /** Progress through Open to Progress to Completed, for the stepper strip. */
  readonly stage = computed(() => {
    switch (this.deal()?.dealState) {
      case DealState.Open:
        return 1;
      case DealState.Progress:
        return 2;
      case DealState.Completed:
        return 3;
      default:
        return 0;
    }
  });

  readonly reload = (): void => this.load();

  constructor() {
    queueMicrotask(() => {
      this.load();
      void this.realtime.joinDeal(this.id());
    });

    this.destroyRef.onDestroy(() => void this.realtime.leaveDeal(this.id()));

    this.realtime.messages$.subscribe((message: Message) => {
      if (message.dealId !== this.id()) {
        return;
      }

      this.detail.update((current) => {
        if (!current || current.messages.some((item) => item.id === message.id)) {
          return current;
        }
        return { ...current, messages: [...current.messages, message] };
      });
    });

    // A transition triggered by the other party re-reads the whole detail, so
    // the timeline and the allowed actions stay in step with the server.
    this.realtime.dealState$.subscribe((event) => {
      if (event.dealId === this.id()) {
        this.load();
      }
    });
  }

  private load(): void {
    this.loading.set(true);

    this.api.deal(this.id()).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
        void this.router.navigate(['/deals']);
      },
    });
  }

  send(body: string): void {
    this.sending.set(true);

    this.api.postDealMessage(this.id(), body).subscribe({
      next: (message) => {
        this.sending.set(false);
        this.detail.update((current) => {
          if (!current || current.messages.some((item) => item.id === message.id)) {
            return current;
          }
          return { ...current, messages: [...current.messages, message] };
        });
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.toast.error(error);
      },
    });
  }

  startProgress(): void {
    this.busy.set(true);

    this.api.startProgress(this.id(), null).subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.detail.set(detail);
        this.toast.success('Marked as in progress. The other party has been notified.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.error(error);
      },
    });
  }

  confirmSettlement(): void {
    this.busy.set(true);

    this.api.confirmSettlement(this.id()).subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.detail.set(detail);

        if (detail.deal.dealState === DealState.Completed) {
          this.toast.success(
            'Both of you confirmed, so the deal is Completed. Rate your counterparty to close the record.',
          );
        } else {
          this.toast.info(
            'Your confirmation is recorded. The deal completes once the other party confirms too.',
          );
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.error(error);
      },
    });
  }

  halt(): void {
    const deal = this.deal();
    if (deal) {
      this.actions.halt(deal, this.reload);
    }
  }

  rate(): void {
    const deal = this.deal();
    if (deal) {
      this.actions.rate(deal, this.reload);
    }
  }

  requestResume(): void {
    const deal = this.deal();
    if (deal) {
      this.actions.requestResume(deal, this.reload);
    }
  }

  acceptResume(): void {
    const deal = this.deal();
    if (deal) {
      this.actions.acceptResume(deal, this.reload);
    }
  }

  declineResume(): void {
    const deal = this.deal();
    const request = deal?.pendingResumeRequest;

    if (!deal || !request) {
      return;
    }

    this.api.respondToResume(deal.id, request.id, false).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.toast.info('You declined the resume request. The deal stays halted.');
      },
      error: (error: unknown) => this.toast.error(error),
    });
  }
}
