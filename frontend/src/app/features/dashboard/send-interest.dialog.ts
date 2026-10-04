import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { DealCategory, ProfileSummary } from '../../core/models/domain';
import { CATEGORY_META } from '../../core/models/status-vocabulary';
import { formatRange } from '../../core/util/format';
import { StarRating } from '../../shared/star-rating';
import { VerificationBadge } from '../../shared/verification-badge';

export interface SendInterestDialogData {
  profile: ProfileSummary;
}

/**
 * Sending interest is the first move in the whole flow, so the dialog restates
 * who the counterparty is and what happens next: a notification, then chat only
 * if they accept.
 */
@Component({
  selector: 'sk-send-interest-dialog',
  imports: [MatDialogModule, FormsModule, StarRating, VerificationBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Send interest to {{ data.profile.name }}</h2>

    <div mat-dialog-content class="content">
      <div class="party">
        <div class="party__head">
          <span class="party__name">{{ data.profile.name }}</span>
          <sk-verification-badge
            [verified]="data.profile.gstinVerified"
            [legalName]="data.profile.gstinLegalName"
            [gstin]="data.profile.gstinMasked"
            size="sm"
          />
        </div>
        <sk-star-rating
          [value]="data.profile.trust.averageStars ?? 0"
          [count]="data.profile.trust.ratingsReceived"
        />
        <dl class="party__facts">
          <div>
            <dt>Deals in</dt>
            <dd>{{ data.profile.subType?.displayName ?? categoryMeta[data.profile.category].label }}</dd>
          </div>
          <div>
            <dt>Capacity</dt>
            <dd>{{ capacity }}</dd>
          </div>
          <div>
            <dt>Based in</dt>
            <dd>{{ data.profile.district }}, {{ data.profile.state }}</dd>
          </div>
          <div>
            <dt>Settled</dt>
            <dd>{{ data.profile.trust.completedDeals }} of {{ data.profile.trust.totalDeals }}</dd>
          </div>
        </dl>
      </div>

      <div class="sk-field">
        <label for="interest-note">Add a short note <span class="sk-hint">(optional)</span></label>
        <textarea
          id="interest-note"
          class="sk-textarea"
          rows="3"
          maxlength="1000"
          [(ngModel)]="note"
          [attr.placeholder]="placeholder"
        ></textarea>
        <p class="sk-hint">
          Say what you are looking for and roughly when. A specific note gets accepted more often
          than a blank one.
        </p>
      </div>

      <p class="next">
        <span class="material-symbols-rounded" aria-hidden="true">east</span>
        <span>
          They get a notification straight away. Chat opens only if they accept, and no deal exists
          until one of you raises a ticket.
        </span>
      </p>
    </div>

    <div mat-dialog-actions class="actions">
      <button type="button" class="sk-btn sk-btn--secondary" (click)="cancel()">Cancel</button>
      <button type="button" class="sk-btn sk-btn--primary" (click)="send()">
        <span class="material-symbols-rounded" aria-hidden="true">send</span>
        Send interest
      </button>
    </div>
  `,
  styles: [
    `
      .content {
        display: grid;
        gap: var(--sk-space-4);
        max-width: 32rem;
      }

      .party {
        display: grid;
        gap: var(--sk-space-2);
        padding: var(--sk-space-3);
        background: var(--sk-surface-sunken);
        border-radius: var(--sk-radius);
      }

      .party__head {
        display: flex;
        align-items: center;
        gap: var(--sk-space-2);
        flex-wrap: wrap;
      }

      .party__name {
        font-family: var(--sk-font-display);
        font-size: 1.125rem;
        font-weight: 600;
        color: var(--sk-ink-strong);
      }

      .party__facts {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--sk-space-2);
        margin: 0;
        padding-top: var(--sk-space-2);
        border-top: 1px solid var(--sk-line);
      }

      .party__facts dt {
        font-size: 0.625rem;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--sk-ink-subtle);
      }

      .party__facts dd {
        margin: 0;
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--sk-ink);
      }

      .next {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3);
        border-radius: var(--sk-radius);
        background: var(--sk-brand-soft);
        color: var(--sk-brand-soft-ink);
        font-size: 0.8125rem;
        line-height: 1.55;
      }

      .next .material-symbols-rounded {
        font-size: 1.125rem;
        flex: none;
      }

      .actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--sk-space-2);
        padding: var(--sk-space-4) !important;
      }
    `,
  ],
})
export class SendInterestDialog {
  readonly data = inject<SendInterestDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<SendInterestDialog, string | null | undefined>>(MatDialogRef);

  readonly categoryMeta = CATEGORY_META;

  note = '';

  readonly capacity = formatRange(
    this.data.profile.capacityMin,
    this.data.profile.capacityMax,
    this.data.profile.capacityUnit,
    this.data.profile.category,
  );

  readonly placeholder =
    this.data.profile.category === DealCategory.Money
      ? 'e.g. Looking for 60,000 for festival stock, can settle within 30 days.'
      : 'e.g. Need around 200 kg weekly from next month, 30-day terms.';

  cancel(): void {
    // undefined means cancelled; null means "send with no note".
    this.dialogRef.close(undefined);
  }

  send(): void {
    this.dialogRef.close(this.note.trim() || null);
  }
}
