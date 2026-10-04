import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { VerificationStatus } from '../core/models/domain';
import { VERIFICATION_STATUS } from '../core/models/status-vocabulary';

/**
 * The persistent verification banner.
 *
 * All four states live here: Needs Approval, Pending, Rejected and Active. It is
 * deliberately the loudest element on the screen while an account is locked,
 * because the user cannot do anything else until it is resolved, and it tells
 * them exactly which action clears it.
 *
 * In the Active state it renders nothing: once there is nothing to do, the
 * banner disappears rather than becoming decorative chrome.
 */
@Component({
  selector: 'sk-verification-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (status() !== VerificationStatus.Active) {
      <aside
        class="banner"
        [style.--banner-color]="'var(--sk-' + descriptor().token + ')'"
        [style.--banner-bg]="'var(--sk-' + descriptor().token + '-soft)'"
        [style.--banner-ink]="'var(--sk-' + descriptor().token + '-soft-ink)'"
        role="status"
      >
        <span class="banner__icon material-symbols-rounded" aria-hidden="true">{{
          descriptor().icon
        }}</span>

        <div class="banner__body">
          <p class="banner__title">{{ title() }}</p>
          <p class="banner__text">{{ text() }}</p>

          @if (status() === VerificationStatus.Rejected && rejectionReason()) {
            <p class="banner__reason">
              <span class="sk-eyebrow">Reviewer's reason</span>
              <span>{{ rejectionReason() }}</span>
            </p>
          }

          <p class="banner__locked">
            <span class="material-symbols-rounded" aria-hidden="true">lock</span>
            Opportunity, Interests, Deals, History and Analytics stay locked until this is
            approved.
          </p>
        </div>

        @if (actionLabel()) {
          <button type="button" class="sk-btn sk-btn--primary banner__action" (click)="action.emit()">
            <span class="material-symbols-rounded" aria-hidden="true">upload_file</span>
            {{ actionLabel() }}
          </button>
        }
      </aside>
    }
  `,
  styles: [
    `
      .banner {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--sk-space-3);
        padding: var(--sk-space-4);
        background: var(--banner-bg);
        color: var(--banner-ink);
        border-radius: var(--sk-radius-lg);
        /* A heavy left rule plus a full ring: the banner has to be impossible to
           scroll past, and this reads as a stamped notice, not a toast. */
        box-shadow: inset 0 0 0 1.5px var(--banner-color),
          inset 6px 0 0 0 var(--banner-color);
      }

      @media (min-width: 760px) {
        .banner {
          grid-template-columns: auto 1fr auto;
          align-items: center;
          padding: var(--sk-space-4) var(--sk-space-5);
        }
      }

      .banner__icon {
        font-size: 1.75rem;
        line-height: 1;
        color: var(--banner-color);
        font-variation-settings: 'FILL' 1;
      }

      .banner__body {
        min-width: 0;
      }

      .banner__title {
        font-family: var(--sk-font-display);
        font-size: 1.125rem;
        font-weight: 600;
        margin: 0 0 2px;
        color: inherit;
      }

      .banner__text {
        margin: 0;
        font-size: 0.9375rem;
      }

      .banner__reason {
        display: grid;
        gap: 2px;
        margin: var(--sk-space-3) 0 0;
        padding: var(--sk-space-3);
        background: rgb(255 255 255 / 55%);
        border-radius: var(--sk-radius);
        font-size: 0.9375rem;
      }

      .banner__reason .sk-eyebrow {
        color: inherit;
        opacity: 0.75;
      }

      .banner__locked {
        display: flex;
        align-items: center;
        gap: 6px;
        margin: var(--sk-space-3) 0 0;
        font-size: 0.8125rem;
        font-weight: 600;
        opacity: 0.9;
      }

      .banner__locked .material-symbols-rounded {
        font-size: 1rem;
      }

      .banner__action {
        grid-column: 1 / -1;
        width: 100%;
      }

      @media (min-width: 760px) {
        .banner__action {
          grid-column: auto;
          width: auto;
          white-space: nowrap;
        }
      }
    `,
  ],
})
export class VerificationBanner {
  readonly VerificationStatus = VerificationStatus;

  readonly status = input.required<VerificationStatus>();
  readonly rejectionReason = input<string | null>(null);
  readonly lastSubmittedAt = input<string | null>(null);

  readonly action = output<void>();

  readonly descriptor = computed(() => VERIFICATION_STATUS[this.status()]);

  readonly title = computed(() => {
    switch (this.status()) {
      case VerificationStatus.NeedsApproval:
        return 'Submit proof documents to open your account';
      case VerificationStatus.Pending:
        return 'Your documents are with a reviewer';
      case VerificationStatus.Rejected:
        return 'Your documents were not approved';
      default:
        return '';
    }
  });

  readonly text = computed(() => {
    switch (this.status()) {
      case VerificationStatus.NeedsApproval:
        return 'You signed up without a GSTIN, so an administrator needs to check who you are. Upload a shop licence, an ID, or a recent utility bill, and your account opens once it is approved.';
      case VerificationStatus.Pending:
        return 'Nothing more is needed from you right now. We will notify you here the moment a decision is made.';
      case VerificationStatus.Rejected:
        return 'Read the reason below, then upload a clearer or different document. Resubmitting puts you straight back in the review queue.';
      default:
        return '';
    }
  });

  readonly actionLabel = computed(() => {
    switch (this.status()) {
      case VerificationStatus.NeedsApproval:
        return 'Upload documents';
      case VerificationStatus.Rejected:
        return 'Resubmit documents';
      default:
        // Nothing to do while Pending: offering a button would invite the user
        // to resubmit over a review that is already under way.
        return null;
    }
  });
}
