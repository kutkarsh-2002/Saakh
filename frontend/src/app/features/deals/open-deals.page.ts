import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { DealRow, DealState } from '../../core/models/domain';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { ToastService } from '../../core/util/toast.service';
import { DealTable } from '../../shared/deal-table';
import { EmptyState, LoadingBlock, PageHeader, StatTile } from '../../shared/ui';
import { DealActions } from './deal-actions';

/**
 * The Open Deals view: everything in Open or Progress, grouped so the deals
 * that need something from this user come first.
 */
@Component({
  selector: 'sk-open-deals-page',
  imports: [RouterLink, PageHeader, StatTile, EmptyState, LoadingBlock, DealTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sk-page">
      <sk-page-header
        eyebrow="Deals"
        title="Open deals"
        subtitle="Everything still in flight. There is no cap on how many you hold at once across different counterparties."
      >
        <a class="sk-btn sk-btn--secondary" routerLink="/history">
          <span class="material-symbols-rounded" aria-hidden="true">history</span>
          Settled &amp; halted
        </a>
        <a class="sk-btn sk-btn--primary" routerLink="/dashboard">
          <span class="material-symbols-rounded" aria-hidden="true">explore</span>
          Find more
        </a>
      </sk-page-header>

      <div class="sk-grid-auto tiles">
        <sk-stat-tile
          label="Open"
          [value]="openCount()"
          icon="radio_button_unchecked"
          tone="open"
          hint="Terms agreed, transfer not started"
        />
        <sk-stat-tile
          label="In progress"
          [value]="progressCount()"
          icon="sync"
          tone="progress"
          hint="Money or material moving"
        />
        <sk-stat-tile
          label="Settling this week"
          [value]="dueSoon()"
          icon="event_upcoming"
          tone="accent"
          hint="Due within seven days"
        />
        <sk-stat-tile
          label="Overdue"
          [value]="overdue()"
          icon="warning"
          tone="halted"
          [hint]="
            overdue() > 0
              ? 'Past the estimated settlement date'
              : 'Nothing past its settlement date'
          "
        />
      </div>

      <section class="sk-card block">
        <div class="sk-card-head">
          <div>
            <h2>In flight</h2>
            <p class="sk-meta">
              Sorted by settlement date, so whatever is closest to due sits at the top.
            </p>
          </div>
        </div>

        @if (loading()) {
          <sk-loading [count]="4" label="Loading your deals" />
        } @else if (deals().length === 0) {
          <sk-empty-state
            icon="receipt_long"
            title="No deals in flight"
            message="Send interest from the Opportunity dashboard. Once it is accepted and you agree terms, raising a ticket creates the deal and it appears here."
          >
            <a class="sk-btn sk-btn--primary" routerLink="/dashboard">Find counterparties</a>
          </sk-empty-state>
        } @else {
          <div class="pad">
            <sk-deal-table
              [rows]="deals()"
              caption="Your open deals"
              (rate)="actions.rate($event, reload)"
              (resume)="actions.requestResume($event, reload)"
              (acceptResume)="actions.acceptResume($event, reload)"
            />
          </div>
        }
      </section>
    </div>
  `,
  styles: [
    `
      .tiles {
        margin-bottom: var(--sk-space-5);
      }

      .block {
        margin-top: var(--sk-space-4);
      }

      .pad {
        padding: var(--sk-space-3) var(--sk-space-4) var(--sk-space-4);
      }
    `,
  ],
})
export class OpenDealsPage {
  private readonly api = inject(SaakhApi);
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);

  readonly actions = inject(DealActions);

  readonly loading = signal(true);
  readonly deals = signal<DealRow[]>([]);

  readonly openCount = computed(
    () => this.deals().filter((deal) => deal.dealState === DealState.Open).length,
  );

  readonly progressCount = computed(
    () => this.deals().filter((deal) => deal.dealState === DealState.Progress).length,
  );

  readonly dueSoon = computed(
    () => this.deals().filter((deal) => this.daysLeft(deal) >= 0 && this.daysLeft(deal) <= 7).length,
  );

  readonly overdue = computed(() => this.deals().filter((deal) => this.daysLeft(deal) < 0).length);

  /** Bound as a field so it can be passed straight to the shared deal actions. */
  readonly reload = (): void => this.load();

  constructor() {
    this.load();
    this.realtime.dealState$.subscribe(() => this.load());
  }

  private daysLeft(deal: DealRow): number {
    return Math.ceil((new Date(deal.estimatedSettlementTime).getTime() - Date.now()) / 86400000);
  }

  private load(): void {
    this.loading.set(true);
    this.api.openDeals().subscribe({
      next: (rows) => {
        this.deals.set(rows);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
      },
    });
  }
}
