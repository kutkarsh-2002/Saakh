import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DealState, DealStateHistory } from '../core/models/domain';
import { DEAL_STATE_STATUS } from '../core/models/status-vocabulary';
import { formatDateTime } from '../core/util/format';

/**
 * The deal's state history, drawn as a vertical timeline.
 *
 * The design brief calls this the single most important interaction pattern in
 * the product: a user has to be able to reconstruct a deal's history at a
 * glance, the way a repayment schedule is the key view in a lending platform.
 * So every transition is shown with who triggered it and when, including the
 * confirmations that did not themselves move the state.
 */
@Component({
  selector: 'sk-deal-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="timeline">
      @for (entry of entries(); track entry.id) {
        <li
          class="timeline__item"
          [class.timeline__item--note]="entry.isNote"
          [style.--entry-color]="'var(--sk-' + entry.token + ')'"
        >
          <span class="timeline__marker" aria-hidden="true">
            <span class="material-symbols-rounded">{{ entry.icon }}</span>
          </span>

          <div class="timeline__body">
            <p class="timeline__label">
              {{ entry.label }}
              @if (entry.isNote) {
                <span class="timeline__tag">no state change</span>
              }
            </p>

            @if (entry.note) {
              <p class="timeline__note">{{ entry.note }}</p>
            }

            <p class="timeline__meta sk-meta">
              @if (entry.actor) {
                <span>{{ entry.actor }}</span>
                <span aria-hidden="true">·</span>
              }
              <time [attr.datetime]="entry.occurredAt">{{ entry.when }}</time>
            </p>
          </div>
        </li>
      }
    </ol>
  `,
  styles: [
    `
      .timeline {
        list-style: none;
        margin: 0;
        padding: 0;
        display: grid;
      }

      .timeline__item {
        position: relative;
        display: grid;
        grid-template-columns: 2rem 1fr;
        gap: var(--sk-space-3);
        padding-bottom: var(--sk-space-4);
      }

      /* The connecting rule is drawn behind the markers and stops at the last
         entry, so the timeline reads as a chain rather than a dangling line. */
      .timeline__item::before {
        content: '';
        position: absolute;
        left: 0.9375rem;
        top: 1.9rem;
        bottom: 0;
        width: 2px;
        background: var(--sk-line);
      }

      .timeline__item:last-child {
        padding-bottom: 0;
      }

      .timeline__item:last-child::before {
        display: none;
      }

      .timeline__marker {
        display: grid;
        place-items: center;
        width: 2rem;
        height: 2rem;
        border-radius: 50%;
        background: var(--entry-color);
        color: var(--sk-ink-inverse);
        box-shadow: 0 0 0 3px var(--sk-surface);
      }

      .timeline__marker .material-symbols-rounded {
        font-size: 1.125rem;
      }

      /* A confirmation that did not move the state is drawn hollow, so a reader
         can tell real transitions from progress notes at a glance. */
      .timeline__item--note .timeline__marker {
        background: var(--sk-surface);
        color: var(--entry-color);
        box-shadow: 0 0 0 2px var(--sk-line-strong), 0 0 0 5px var(--sk-surface);
      }

      .timeline__body {
        min-width: 0;
        padding-top: 2px;
      }

      .timeline__label {
        margin: 0;
        font-weight: 600;
        font-size: 0.9375rem;
        color: var(--sk-ink-strong);
      }

      .timeline__tag {
        display: inline-block;
        margin-left: 6px;
        padding: 1px 7px;
        border-radius: var(--sk-radius-pill);
        background: var(--sk-surface-sunken);
        color: var(--sk-ink-muted);
        font-size: 0.6875rem;
        font-weight: 600;
        vertical-align: 2px;
      }

      .timeline__note {
        margin: 2px 0 0;
        font-size: 0.875rem;
        color: var(--sk-ink-muted);
      }

      .timeline__meta {
        display: flex;
        gap: 6px;
        margin: 4px 0 0;
        flex-wrap: wrap;
      }
    `,
  ],
})
export class DealTimeline {
  readonly history = input.required<DealStateHistory[]>();

  readonly entries = computed(() =>
    this.history().map((entry) => {
      const descriptor = DEAL_STATE_STATUS[entry.toState];
      // The API records settlement confirmations as same-state rows, so the
      // timeline can show "waiting on the other party" without inventing a state.
      const isNote = entry.fromState !== null && entry.fromState === entry.toState;

      return {
        id: entry.id,
        token: isNote ? 'inactive' : descriptor.token,
        icon: isNote ? 'how_to_reg' : descriptor.icon,
        label: isNote ? 'Settlement confirmed by one party' : labelFor(entry.fromState, entry.toState),
        note: entry.note,
        actor: entry.triggeredByName,
        occurredAt: entry.occurredAt,
        when: formatDateTime(entry.occurredAt),
        isNote,
      };
    }),
  );
}

function labelFor(from: DealState | null, to: DealState): string {
  const target = DEAL_STATE_STATUS[to].label;

  if (from === null) {
    return 'Ticket raised — deal opened';
  }

  return `${DEAL_STATE_STATUS[from].label} → ${target}`;
}
