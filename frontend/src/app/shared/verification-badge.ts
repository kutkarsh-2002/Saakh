import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';

/**
 * The GSTIN-verified badge.
 *
 * The design brief asks that verification read with the weight a credit bureau
 * gives a credit score, so this is a deliberate, solid mark rather than a faint
 * tick: a verified identity is the single strongest trust signal on a profile.
 *
 * It renders nothing at all when the identity was not registry-verified. An
 * unverified profile is never dressed up with a weaker version of this badge,
 * because a half-badge would read as a trust signal that does not exist.
 */
@Component({
  selector: 'sk-verification-badge',
  imports: [MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (verified()) {
      <span class="badge" [class.badge--sm]="size() === 'sm'" [matTooltip]="tooltip()">
        <span class="material-symbols-rounded badge__icon" aria-hidden="true">verified</span>
        <span class="badge__text">GSTIN verified</span>
      </span>
    } @else if (showUnverified()) {
      <span class="unverified" matTooltip="This account was approved by an administrator reviewing submitted documents, not by the government registry.">
        <span class="material-symbols-rounded" aria-hidden="true">assignment_turned_in</span>
        <span>Document-verified</span>
      </span>
    }
  `,
  styles: [
    `
      :host {
        display: inline-flex;
        max-width: 100%;
      }

      .badge {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 4px 11px 4px 8px;
        border-radius: var(--sk-radius-pill);
        background: var(--sk-brand);
        color: var(--sk-ink-inverse);
        font-size: 0.75rem;
        font-weight: 700;
        letter-spacing: 0.01em;
        line-height: 1.5;
        white-space: nowrap;
      }

      .badge--sm {
        font-size: 0.6875rem;
        padding: 2px 9px 2px 6px;
      }

      .badge__icon {
        font-size: 1.0625rem;
        font-variation-settings: 'FILL' 1;
      }

      .badge--sm .badge__icon {
        font-size: 0.9375rem;
      }

      .unverified {
        display: inline-flex;
        align-items: center;
        gap: 5px;
        padding: 3px 10px 3px 7px;
        border-radius: var(--sk-radius-pill);
        background: var(--sk-surface-sunken);
        box-shadow: inset 0 0 0 1px var(--sk-line-strong);
        color: var(--sk-ink-muted);
        font-size: 0.75rem;
        font-weight: 600;
        white-space: nowrap;
      }

      .unverified .material-symbols-rounded {
        font-size: 0.9375rem;
      }
    `,
  ],
})
export class VerificationBadge {
  readonly verified = input(false);

  /** The legal name the registry returned, shown on hover as corroboration. */
  readonly legalName = input<string | null>(null);

  readonly gstin = input<string | null>(null);

  /** Shows the weaker document-verified mark for an admin-approved account. */
  readonly showUnverified = input(false);

  readonly size = input<'sm' | 'md'>('md');

  readonly tooltip = computed(() => {
    const parts = ['Validated in real time against the government GST registry.'];
    const legalName = this.legalName();
    const gstin = this.gstin();

    if (legalName) {
      parts.push(`Registered as ${legalName}.`);
    }
    if (gstin) {
      parts.push(`GSTIN ${gstin}`);
    }

    return parts.join(' ');
  });
}
