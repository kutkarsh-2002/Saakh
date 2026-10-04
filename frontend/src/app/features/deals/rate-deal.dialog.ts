import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { DealRow, DealState } from '../../core/models/domain';
import { StarRating } from '../../shared/star-rating';
import { StatusPill } from '../../shared/status-pill';

export interface RateDealDialogData {
  deal: DealRow;
}

export interface RateDealResult {
  stars: number;
  comment: string | null;
}

/**
 * Rating is the mechanism that makes trust portable, so this dialog says what
 * the rating is for and reminds the rater that it is permanent and public —
 * one rating per party per deal, and there is no edit afterwards.
 */
@Component({
  selector: 'sk-rate-deal-dialog',
  imports: [MatDialogModule, FormsModule, StarRating, StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Rate {{ data.deal.counterparty.name }}</h2>

    <div mat-dialog-content class="content">
      <div class="deal">
        <span class="deal__ref sk-figure">{{ data.deal.reference }}</span>
        <sk-status-pill [dealState]="data.deal.dealState" />
      </div>

      <p class="prompt">
        @if (data.deal.dealState === DealState.Completed) {
          This deal settled. How was {{ data.deal.counterparty.name }} to deal with?
        } @else {
          This deal was halted
          @if (data.deal.haltedByName) {
            by {{ data.deal.haltedByName }}.
          } @else {
            .
          }
          Rate how {{ data.deal.counterparty.name }} handled it.
        }
      </p>

      <div class="rate">
        <sk-star-rating
          [editable]="true"
          [value]="stars()"
          (picked)="stars.set($event)"
          name="deal-rating"
          [legend]="'Rate ' + data.deal.counterparty.name + ' from 1 to 5 stars'"
        />
        <p class="rate__meaning">{{ meaning() }}</p>
      </div>

      <div class="sk-field">
        <label for="rating-comment">Add a note <span class="sk-hint">(optional)</span></label>
        <textarea
          id="rating-comment"
          class="sk-textarea"
          rows="3"
          maxlength="1000"
          [(ngModel)]="comment"
          placeholder="e.g. Paid two days before the due date, no follow-up needed."
        ></textarea>
        <p class="sk-hint">
          Concrete notes about timing and communication help the next counterparty far more than a
          general comment.
        </p>
      </div>

      <p class="warn">
        <span class="material-symbols-rounded" aria-hidden="true">push_pin</span>
        <span>
          You get one rating per deal and it cannot be changed afterwards. It becomes part of
          {{ data.deal.counterparty.name }}'s public trust record.
        </span>
      </p>
    </div>

    <div mat-dialog-actions class="actions">
      <button type="button" class="sk-btn sk-btn--secondary" (click)="close()">Cancel</button>
      <button
        type="button"
        class="sk-btn sk-btn--primary"
        [disabled]="stars() === 0"
        (click)="submit()"
      >
        Submit rating
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

      .deal {
        display: flex;
        align-items: center;
        gap: var(--sk-space-2);
        flex-wrap: wrap;
      }

      .deal__ref {
        font-weight: 700;
        color: var(--sk-brand);
      }

      .prompt {
        margin: 0;
        font-size: 0.9375rem;
      }

      .rate {
        display: grid;
        justify-items: center;
        gap: var(--sk-space-1);
        padding: var(--sk-space-3);
        background: var(--sk-surface-sunken);
        border-radius: var(--sk-radius);
      }

      .rate__meaning {
        margin: 0;
        min-height: 1.4em;
        font-weight: 600;
        font-size: 0.875rem;
        color: var(--sk-ink);
      }

      .warn {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3);
        border-radius: var(--sk-radius);
        background: var(--sk-needs-approval-soft);
        color: var(--sk-needs-approval-soft-ink);
        font-size: 0.8125rem;
        line-height: 1.55;
      }

      .warn .material-symbols-rounded {
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
export class RateDealDialog {
  readonly data = inject<RateDealDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<RateDealDialog, RateDealResult | null>>(MatDialogRef);

  readonly DealState = DealState;

  readonly stars = signal(0);
  comment = '';

  /** Says what each score means, so a 3 means the same thing from every rater. */
  meaning(): string {
    switch (this.stars()) {
      case 5:
        return 'Settled as agreed, no chasing needed';
      case 4:
        return 'Settled, with a small slip';
      case 3:
        return 'Settled late or after reminders';
      case 2:
        return 'Pulled back or went quiet';
      case 1:
        return 'Walked away from the arrangement';
      default:
        return 'Pick a score';
    }
  }

  close(): void {
    this.dialogRef.close(null);
  }

  submit(): void {
    if (this.stars() === 0) {
      return;
    }

    this.dialogRef.close({ stars: this.stars(), comment: this.comment.trim() || null });
  }
}
