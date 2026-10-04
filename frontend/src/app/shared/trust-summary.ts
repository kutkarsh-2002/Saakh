import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TrustSummary } from '../core/models/domain';
import { StarRating } from './star-rating';

/**
 * The trust record: the thing this whole product exists to make portable.
 *
 * It reports the full settled history rather than a single blended average,
 * which is what the spec asks for ("12 deals, 11 rated 5 stars, 1 halted"), and
 * it states the at-fault halt count plainly instead of burying it. A trust
 * record that only shows the good numbers is not a trust record.
 */
@Component({
  selector: 'sk-trust-summary',
  imports: [StarRating, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (compact()) {
      <span class="compact">
        <sk-star-rating [value]="average()" [count]="trust().ratingsReceived" />
        <span class="compact__deals sk-meta">{{ dealsLabel() }}</span>
        @if (trust().haltsAtFault > 0) {
          <span
            class="compact__fault"
            matTooltip="Deals this profile halted. Under the v1 rule, whoever triggers a halt is recorded at fault for it."
          >
            <span class="material-symbols-rounded" aria-hidden="true">flag</span>
            {{ trust().haltsAtFault }} at fault
          </span>
        }
      </span>
    } @else {
      <section class="full">
        <header class="full__head">
          <div>
            <p class="sk-eyebrow">Trust record</p>
            <sk-star-rating [value]="average()" [count]="trust().ratingsReceived" size="lg" />
          </div>

          <p class="full__headline">{{ headline() }}</p>
        </header>

        @if (trust().ratingsReceived > 0) {
          <div class="bars" role="list" aria-label="Rating distribution">
            @for (row of distribution(); track row.stars) {
              <div class="bars__row" role="listitem">
                <span class="bars__label sk-figure">{{ row.stars }}</span>
                <span class="material-symbols-rounded bars__star" aria-hidden="true">star</span>
                <span class="bars__track">
                  <span class="bars__fill" [style.width.%]="row.percent"></span>
                </span>
                <span class="bars__count sk-figure">{{ row.count }}</span>
              </div>
            }
          </div>
        }

        @if (showCounts()) {
        <dl class="counts">
          <div class="counts__item">
            <dt>Deals</dt>
            <dd class="sk-figure">{{ trust().totalDeals }}</dd>
          </div>
          <div class="counts__item counts__item--good">
            <dt>Settled clean</dt>
            <dd class="sk-figure">{{ trust().completedDeals }}</dd>
          </div>
          <div class="counts__item counts__item--warn">
            <dt>Halted</dt>
            <dd class="sk-figure">{{ trust().haltedDeals }}</dd>
          </div>
          <div class="counts__item">
            <dt>In flight</dt>
            <dd class="sk-figure">{{ trust().activeDeals }}</dd>
          </div>
          <div
            class="counts__item"
            [class.counts__item--warn]="trust().haltsAtFault > 0"
            matTooltip="Under the v1 rule, whoever triggers a halt is auto-flagged at fault for that deal."
          >
            <dt>Halts at fault</dt>
            <dd class="sk-figure">{{ trust().haltsAtFault }}</dd>
          </div>
        </dl>
        }
      </section>
    }
  `,
  styles: [
    `
      .compact {
        display: inline-flex;
        align-items: center;
        gap: var(--sk-space-2);
        flex-wrap: wrap;
      }

      .compact__deals {
        white-space: nowrap;
      }

      .compact__fault {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        padding: 1px 8px 1px 5px;
        border-radius: var(--sk-radius-pill);
        background: var(--sk-halted-soft);
        color: var(--sk-halted-soft-ink);
        box-shadow: inset 0 0 0 1px var(--sk-halted);
        font-size: 0.6875rem;
        font-weight: 700;
        white-space: nowrap;
      }

      .compact__fault .material-symbols-rounded {
        font-size: 0.875rem;
      }

      .full {
        display: grid;
        gap: var(--sk-space-4);
      }

      .full__head {
        display: flex;
        align-items: flex-end;
        justify-content: space-between;
        gap: var(--sk-space-4);
        flex-wrap: wrap;
      }

      .full__headline {
        margin: 0;
        font-family: var(--sk-font-display);
        font-size: 1rem;
        color: var(--sk-ink-muted);
        text-align: right;
        flex: 1 1 14rem;
      }

      .bars {
        display: grid;
        gap: 3px;
      }

      .bars__row {
        display: grid;
        grid-template-columns: 1.1rem 1.1rem 1fr 2rem;
        align-items: center;
        gap: 6px;
        font-size: 0.75rem;
      }

      .bars__label {
        text-align: right;
        color: var(--sk-ink-muted);
      }

      .bars__star {
        font-size: 0.875rem;
        color: var(--sk-star);
        font-variation-settings: 'FILL' 1;
      }

      .bars__track {
        height: 8px;
        background: var(--sk-surface-sunken);
        border-radius: var(--sk-radius-pill);
        overflow: hidden;
      }

      .bars__fill {
        display: block;
        height: 100%;
        background: var(--sk-star);
        border-radius: var(--sk-radius-pill);
      }

      .bars__count {
        text-align: right;
        color: var(--sk-ink-muted);
      }

      .counts {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(5.5rem, 1fr));
        gap: var(--sk-space-2);
        margin: 0;
      }

      .counts__item {
        padding: var(--sk-space-2) var(--sk-space-3);
        background: var(--sk-surface-sunken);
        border-radius: var(--sk-radius);
      }

      .counts__item--good {
        background: var(--sk-completed-soft);
      }

      .counts__item--warn {
        background: var(--sk-halted-soft);
      }

      .counts__item dt {
        font-size: 0.6875rem;
        font-weight: 600;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--sk-ink-muted);
      }

      .counts__item dd {
        margin: 0;
        font-size: 1.375rem;
        line-height: 1.2;
        color: var(--sk-ink-strong);
      }
    `,
  ],
})
export class TrustSummaryView {
  readonly trust = input.required<TrustSummary>();
  readonly compact = input(false);

  /** Hidden where the host already shows the same figures as stat tiles. */
  readonly showCounts = input(true);

  readonly average = computed(() => this.trust().averageStars ?? 0);

  readonly dealsLabel = computed(() => {
    const trust = this.trust();
    if (trust.totalDeals === 0) {
      return 'No deals yet';
    }
    return `${trust.totalDeals} ${trust.totalDeals === 1 ? 'deal' : 'deals'}`;
  });

  /** The plain-language version of the record, in the spec's own phrasing. */
  readonly headline = computed(() => {
    const trust = this.trust();

    if (trust.totalDeals === 0) {
      return 'No trading history on Saakh yet.';
    }

    const parts = [`${trust.totalDeals} ${trust.totalDeals === 1 ? 'deal' : 'deals'}`];

    if (trust.completedDeals > 0) {
      parts.push(`${trust.completedDeals} settled`);
    }
    if (trust.haltedDeals > 0) {
      parts.push(`${trust.haltedDeals} halted`);
    }
    if (trust.activeDeals > 0) {
      parts.push(`${trust.activeDeals} in flight`);
    }

    return `${parts.join(', ')}.`;
  });

  readonly distribution = computed(() => {
    const counts = this.trust().starCounts ?? [0, 0, 0, 0, 0];
    const max = Math.max(1, ...counts);

    // Highest rating first: a reader scans down from the best score.
    return [5, 4, 3, 2, 1].map((stars) => {
      const count = counts[stars - 1] ?? 0;
      return { stars, count, percent: (count / max) * 100 };
    });
  });
}
