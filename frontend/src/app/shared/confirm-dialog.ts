import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';

export interface ConfirmDialogData {
  title: string;
  message: string;
  /** Shown in a bordered callout: the consequence the user is accepting. */
  consequence?: string;
  confirmLabel: string;
  cancelLabel?: string;
  destructive?: boolean;
  /** When set, asks for a reason and returns it. Required unless optional. */
  reasonLabel?: string;
  reasonPlaceholder?: string;
  reasonRequired?: boolean;
}

export type ConfirmDialogResult = { confirmed: true; reason: string | null } | null;

/**
 * One confirmation dialog for every irreversible action in the product: halting
 * a deal, rejecting a verification, suspending a profile.
 *
 * It always states the consequence rather than asking a bare "are you sure?",
 * because the consequences here are real: halting a deal permanently records
 * the halting party at fault, and the user has to know that before they click.
 */
@Component({
  selector: 'sk-confirm-dialog',
  imports: [MatDialogModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>

    <div mat-dialog-content class="content">
      <p class="content__message">{{ data.message }}</p>

      @if (data.consequence) {
        <p class="content__consequence" [class.content__consequence--warn]="data.destructive">
          <span class="material-symbols-rounded" aria-hidden="true">
            {{ data.destructive ? 'warning' : 'info' }}
          </span>
          <span>{{ data.consequence }}</span>
        </p>
      }

      @if (data.reasonLabel) {
        <div class="sk-field">
          <label for="confirm-reason">
            {{ data.reasonLabel }}
            @if (!data.reasonRequired) {
              <span class="sk-hint">(optional)</span>
            }
          </label>
          <textarea
            id="confirm-reason"
            class="sk-textarea"
            [class.is-invalid]="showReasonError"
            [(ngModel)]="reason"
            [attr.placeholder]="data.reasonPlaceholder ?? ''"
            rows="3"
            maxlength="1000"
          ></textarea>
          @if (showReasonError) {
            <p class="sk-error">
              <span class="material-symbols-rounded" aria-hidden="true">error</span>
              This reason is shown to the other party, so it cannot be left blank.
            </p>
          }
        </div>
      }
    </div>

    <div mat-dialog-actions class="actions">
      <button type="button" class="sk-btn sk-btn--secondary" (click)="cancel()">
        {{ data.cancelLabel ?? 'Cancel' }}
      </button>
      <button
        type="button"
        class="sk-btn"
        [class.sk-btn--destructive]="data.destructive"
        [class.sk-btn--primary]="!data.destructive"
        (click)="confirm()"
      >
        {{ data.confirmLabel }}
      </button>
    </div>
  `,
  styles: [
    `
      .content {
        display: grid;
        gap: var(--sk-space-4);
        max-width: 34rem;
      }

      .content__message {
        margin: 0;
        font-size: 0.9375rem;
        color: var(--sk-ink);
      }

      .content__consequence {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3);
        border-radius: var(--sk-radius);
        background: var(--sk-surface-sunken);
        box-shadow: inset 0 0 0 1px var(--sk-line-strong);
        font-size: 0.875rem;
      }

      .content__consequence--warn {
        background: var(--sk-halted-soft);
        color: var(--sk-halted-soft-ink);
        box-shadow: inset 0 0 0 1.5px var(--sk-halted);
        font-weight: 500;
      }

      .content__consequence .material-symbols-rounded {
        font-size: 1.25rem;
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
export class ConfirmDialog {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<ConfirmDialog, ConfirmDialogResult>>(MatDialogRef);

  reason = '';
  showReasonError = false;

  cancel(): void {
    this.dialogRef.close(null);
  }

  confirm(): void {
    const trimmed = this.reason.trim();

    if (this.data.reasonRequired && !trimmed) {
      this.showReasonError = true;
      return;
    }

    this.dialogRef.close({ confirmed: true, reason: trimmed || null });
  }
}
