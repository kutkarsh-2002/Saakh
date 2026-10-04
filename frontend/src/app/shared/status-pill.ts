import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import {
  AvailabilityStatus,
  DealState,
  InterestStatus,
  VerificationStatus,
} from '../core/models/domain';
import {
  AVAILABILITY_STATUS,
  DEAL_STATE_STATUS,
  INTEREST_STATUS,
  StatusDescriptor,
  VERIFICATION_STATUS,
} from '../core/models/status-vocabulary';

/**
 * The status pill, used for every state in the product.
 *
 * Three signals always travel together: a token-driven colour, an icon whose
 * silhouette is unique to that status, and the status name in words. That is
 * what the design brief means by never conveying status by colour alone, and it
 * is also what makes these readable at arm's length in sunlight.
 */
@Component({
  selector: 'sk-status-pill',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="pill"
      [class.pill--lg]="size() === 'lg'"
      [class.pill--solid]="variant() === 'solid'"
      [style.--pill-color]="'var(--sk-' + descriptor().token + ')'"
      [style.--pill-bg]="'var(--sk-' + descriptor().token + '-soft)'"
      [style.--pill-ink]="'var(--sk-' + descriptor().token + '-soft-ink)'"
      [matTooltip]="showTooltip() ? descriptor().meaning : ''"
      matTooltipPosition="above"
    >
      <span
        class="pill__icon material-symbols-rounded"
        [class.pill__icon--spin]="spinning()"
        aria-hidden="true"
        >{{ descriptor().icon }}</span
      >
      <span class="pill__label">{{ descriptor().label }}</span>
    </span>
  `,
  styles: [
    `
      :host {
        display: inline-flex;
        max-width: 100%;
      }

      .pill {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 3px 10px 3px 7px;
        border-radius: var(--sk-radius-pill);
        background: var(--pill-bg);
        color: var(--pill-ink);
        /* A 1.5px ring in the full-strength colour keeps the pill legible even
           where the soft fill washes out on a cheap, bright screen. */
        box-shadow: inset 0 0 0 1.5px var(--pill-color);
        font-family: var(--sk-font-ui);
        font-size: 0.75rem;
        font-weight: 600;
        line-height: 1.5;
        white-space: nowrap;
      }

      .pill--lg {
        font-size: 0.875rem;
        padding: 6px 14px 6px 10px;
        gap: 8px;
      }

      .pill--solid {
        background: var(--pill-color);
        color: var(--sk-ink-inverse);
        box-shadow: none;
      }

      .pill__icon {
        font-size: 1rem;
        line-height: 1;
        flex: none;
      }

      .pill--lg .pill__icon {
        font-size: 1.25rem;
      }

      /* Progress is the one state the spec asks to show as moving. */
      .pill__icon--spin {
        animation: pill-spin 1.8s linear infinite;
      }

      @keyframes pill-spin {
        to {
          transform: rotate(360deg);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .pill__icon--spin {
          animation: none;
        }
      }

      .pill__label {
        overflow: hidden;
        text-overflow: ellipsis;
      }
    `,
  ],
})
export class StatusPill {
  readonly dealState = input<DealState | null>(null);
  readonly verification = input<VerificationStatus | null>(null);
  readonly availability = input<AvailabilityStatus | null>(null);
  readonly interest = input<InterestStatus | null>(null);

  readonly size = input<'md' | 'lg'>('md');
  readonly variant = input<'soft' | 'solid'>('soft');
  readonly showTooltip = input(true);

  readonly descriptor = computed<StatusDescriptor>(() => {
    const deal = this.dealState();
    if (deal !== null) {
      return DEAL_STATE_STATUS[deal];
    }

    const verification = this.verification();
    if (verification !== null) {
      return VERIFICATION_STATUS[verification];
    }

    const availability = this.availability();
    if (availability !== null) {
      return AVAILABILITY_STATUS[availability];
    }

    const interest = this.interest();
    if (interest !== null) {
      return INTEREST_STATUS[interest];
    }

    return { token: 'inactive', icon: 'help', label: 'Unknown', meaning: '' };
  });

  readonly spinning = computed(() => this.dealState() === DealState.Progress);
}
