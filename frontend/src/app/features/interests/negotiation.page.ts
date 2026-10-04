import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import {
  CategorySubType,
  Interest,
  InterestStatus,
  Message,
} from '../../core/models/domain';
import { CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { formatRange, formatRelative } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { ChatThread } from '../../shared/chat-thread';
import { LoadingBlock } from '../../shared/ui';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';
import { RaiseTicketDialog, RaiseTicketDialogData } from './raise-ticket.dialog';

/**
 * Pre-ticket negotiation: the chat that opens once interest is accepted, with
 * the counterparty's trust record alongside it and the Raise ticket action that
 * turns the conversation into a tracked Deal.
 */
@Component({
  selector: 'sk-negotiation-page',
  imports: [
    RouterLink,
    ChatThread,
    LoadingBlock,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './negotiation.page.html',
  styleUrl: './negotiation.page.scss',
})
export class NegotiationPage {
  /** Bound from the route via withComponentInputBinding(). */
  readonly id = input.required<string>();

  private readonly api = inject(SaakhApi);
  private readonly realtime = inject(RealtimeService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly InterestStatus = InterestStatus;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly formatRelative = formatRelative;

  readonly loading = signal(true);
  readonly sending = signal(false);
  readonly interest = signal<Interest | null>(null);
  readonly messages = signal<Message[]>([]);
  readonly taxonomy = signal<CategorySubType[]>([]);

  constructor() {
    this.api.taxonomy().subscribe({ next: (list) => this.taxonomy.set(list) });

    // input() values are not available in the constructor body on first run, so
    // the load is deferred to a microtask once the binding has been applied.
    queueMicrotask(() => this.load());

    this.realtime.messages$.subscribe((message) => {
      if (message.interestId !== this.id()) {
        return;
      }
      this.messages.update((list) =>
        list.some((item) => item.id === message.id) ? list : [...list, message],
      );
    });

    this.realtime.interest$.subscribe((event) => {
      if (event.interestId === this.id()) {
        this.load();
      }
    });
  }

  private load(): void {
    this.loading.set(true);

    this.api.interest(this.id()).subscribe({
      next: (interest) => {
        this.interest.set(interest);
        this.loading.set(false);

        if (interest.chatUnlocked) {
          this.api.interestMessages(interest.id).subscribe({
            next: (messages) => this.messages.set(messages),
            error: () => undefined,
          });
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
        void this.router.navigate(['/interests']);
      },
    });
  }

  capacityLabel(): string {
    const counterparty = this.interest()?.counterparty;
    if (!counterparty) {
      return '';
    }
    return formatRange(
      counterparty.capacityMin,
      counterparty.capacityMax,
      counterparty.capacityUnit,
      counterparty.category,
    );
  }

  send(body: string): void {
    const interest = this.interest();
    if (!interest) {
      return;
    }

    this.sending.set(true);

    this.api.postInterestMessage(interest.id, body).subscribe({
      next: (message) => {
        this.sending.set(false);
        this.messages.update((list) =>
          list.some((item) => item.id === message.id) ? list : [...list, message],
        );
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.toast.error(error);
      },
    });
  }

  accept(): void {
    const interest = this.interest();
    if (!interest) {
      return;
    }

    this.api.respondToInterest(interest.id, true).subscribe({
      next: () => {
        this.toast.success('Chat is open. Negotiate the terms, then raise a ticket.');
        this.load();
      },
      error: (error: unknown) => this.toast.error(error),
    });
  }

  raiseTicket(): void {
    const interest = this.interest();
    if (!interest) {
      return;
    }

    const data: RaiseTicketDialogData = {
      interestId: interest.id,
      counterparty: interest.counterparty,
      taxonomy: this.taxonomy(),
    };

    this.dialog
      .open(RaiseTicketDialog, { data, width: '620px', maxWidth: '96vw' })
      .afterClosed()
      .subscribe((payload) => {
        if (!payload) {
          return;
        }

        this.api.raiseTicket(payload).subscribe({
          next: (deal) => {
            this.toast.success(`Deal ${deal.reference} is open. Both of you can see it now.`);
            void this.router.navigate(['/deals', deal.id]);
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }
}
