import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { DealRow, DealState } from '../../core/models/domain';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { ToastService } from '../../core/util/toast.service';
import { DealTable } from '../../shared/deal-table';
import { EmptyState, FilterChip, LoadingBlock, PageHeader, StatTile } from '../../shared/ui';
import { DealActions } from './deal-actions';

type Filter = 'all' | 'completed' | 'halted';

/**
 * The History tab: every deal that has left the active states, with the same
 * columns as the open-deals table plus the rating given and received.
 *
 * A halted row can still act: either party can send a "Resume?" request from
 * here, and the deal only returns to Progress once the other side accepts.
 */
@Component({
  selector: 'sk-history-page',
  imports: [
    RouterLink,
    PageHeader,
    StatTile,
    FilterChip,
    EmptyState,
    LoadingBlock,
    DealTable,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sk-page">
      <sk-page-header
        eyebrow="History"
        title="Settled &amp; halted deals"
        subtitle="Your closed record. This is what a new counterparty is looking at when they decide whether to extend you credit."
      >
        <a class="sk-btn sk-btn--secondary" routerLink="/deals">
          <span class="material-symbols-rounded" aria-hidden="true">pending_actions</span>
          Open deals
        </a>
      </sk-page-header>

      <div class="sk-grid-auto tiles">
        <sk-stat-tile
          label="Settled clean"
          [value]="completedCount()"
          icon="task_alt"
          tone="completed"
          hint="Both parties confirmed settlement"
        />
        <sk-stat-tile
          label="Halted"
          [value]="haltedCount()"
          icon="flag"
          tone="halted"
          [hint]="
            myFaultCount() > 0
              ? myFaultCount() + ' triggered by you, and recorded at your fault'
              : 'None triggered by you'
          "
        />
        <sk-stat-tile
          label="Ratings received"
          [value]="ratedCount()"
          icon="star"
          tone="accent"
          [hint]="
            unratedByMe() > 0
              ? unratedByMe() + ' of your own ratings still to give'
              : 'You have rated every closed deal'
          "
        />
        <sk-stat-tile
          label="Awaiting resume"
          [value]="pendingResume()"
          icon="replay"
          tone="pending"
          hint="Resume requests not yet answered"
        />
      </div>

      <section class="sk-card block">
        <div class="sk-card-head">
          <div>
            <h2>Closed deals</h2>
            <p class="sk-meta">Most recently closed first.</p>
          </div>
          <span class="sk-spacer"></span>
          <div class="chips" role="group" aria-label="Filter by outcome">
            <sk-filter-chip
              label="Both"
              [selected]="filter() === 'all'"
              (toggled)="setFilter('all')"
            />
            <sk-filter-chip
              label="Completed"
              icon="task_alt"
              [selected]="filter() === 'completed'"
              (toggled)="setFilter('completed')"
            />
            <sk-filter-chip
              label="Halted"
              icon="flag"
              [selected]="filter() === 'halted'"
              (toggled)="setFilter('halted')"
            />
          </div>
        </div>

        @if (unratedByMe() > 0) {
          <p class="nudge">
            <span class="material-symbols-rounded" aria-hidden="true">star</span>
            <span>
              <strong>
                {{ unratedByMe() }} closed {{ unratedByMe() === 1 ? 'deal' : 'deals' }}
                {{ unratedByMe() === 1 ? 'still needs' : 'still need' }} your rating.
              </strong>
              Rating is what turns a settled deal into a trust signal for the other party, so an
              unrated deal leaves them with nothing to show the next counterparty.
            </span>
          </p>
        }

        @if (loading()) {
          <sk-loading [count]="4" label="Loading your history" />
        } @else if (rows().length === 0) {
          <sk-empty-state
            icon="history"
            [title]="
              filter() === 'all'
                ? 'Nothing has closed yet'
                : filter() === 'completed'
                  ? 'No completed deals yet'
                  : 'No halted deals'
            "
            [message]="
              filter() === 'halted'
                ? 'Nothing of yours has been halted. That is the best possible version of this page.'
                : 'Once a deal reaches Completed or Halted it moves here, along with the ratings both sides gave.'
            "
          >
            @if (filter() !== 'all') {
              <button type="button" class="sk-btn sk-btn--secondary" (click)="setFilter('all')">
                Show both
              </button>
            }
          </sk-empty-state>
        } @else {
          <div class="pad">
            <sk-deal-table
              [rows]="rows()"
              [showRatings]="true"
              caption="Your settled and halted deals"
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

      .chips {
        display: flex;
        gap: var(--sk-space-2);
        flex-wrap: wrap;
      }

      .nudge {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3) var(--sk-space-4);
        background: var(--sk-accent-soft);
        color: var(--sk-accent-soft-ink);
        border-bottom: 1px solid var(--sk-line-hairline);
        font-size: 0.875rem;
        line-height: 1.55;
      }

      .nudge .material-symbols-rounded {
        font-size: 1.25rem;
        flex: none;
        font-variation-settings: 'FILL' 1;
      }

      .pad {
        padding: var(--sk-space-3) var(--sk-space-4) var(--sk-space-4);
      }
    `,
  ],
})
export class HistoryPage {
  private readonly api = inject(SaakhApi);
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);

  readonly actions = inject(DealActions);

  readonly loading = signal(true);
  readonly all = signal<DealRow[]>([]);
  readonly filter = signal<Filter>('all');

  readonly rows = computed(() => {
    const filter = this.filter();
    if (filter === 'completed') {
      return this.all().filter((deal) => deal.dealState === DealState.Completed);
    }
    if (filter === 'halted') {
      return this.all().filter((deal) => deal.dealState === DealState.Halted);
    }
    return this.all();
  });

  readonly completedCount = computed(
    () => this.all().filter((deal) => deal.dealState === DealState.Completed).length,
  );

  readonly haltedCount = computed(
    () => this.all().filter((deal) => deal.dealState === DealState.Halted).length,
  );

  readonly myFaultCount = computed(() => this.all().filter((deal) => deal.iHaltedThisDeal).length);

  readonly ratedCount = computed(() => this.all().filter((deal) => deal.ratingReceived).length);

  readonly unratedByMe = computed(() => this.all().filter((deal) => deal.canRate).length);

  readonly pendingResume = computed(
    () => this.all().filter((deal) => deal.pendingResumeRequest).length,
  );

  readonly reload = (): void => this.load();

  constructor() {
    this.load();
    this.realtime.dealState$.subscribe(() => this.load());
  }

  setFilter(filter: Filter): void {
    this.filter.set(filter);
  }

  private load(): void {
    this.loading.set(true);
    // The whole closed set is fetched once and filtered on the client, so the
    // toggle is instant and the counters above it always agree with the table.
    this.api.history(null).subscribe({
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
}
