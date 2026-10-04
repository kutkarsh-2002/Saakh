import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { SuspensionDuration } from '../../core/models/domain';

export interface SuspendDialogResult {
  duration: SuspensionDuration;
  notes: string | null;
}

/**
 * Suspension needs a window, so the four the spec defines are offered as an
 * explicit choice rather than a free-text duration: 1 week, 2 weeks, 1 month,
 * or permanent.
 */
@Component({
  selector: 'sk-suspend-dialog',
  imports: [MatDialogModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Suspend {{ data.name }}</h2>

    <div mat-dialog-content class="content">
      <p class="intro">
        A suspended profile is hidden from search and cannot send or receive interest for the
        window you pick.
      </p>

      @if (data.activeDeals > 0) {
        <p class="freeze">
          <span class="material-symbols-rounded" aria-hidden="true">ac_unit</span>
          <span>
            <strong>
              {{ data.activeDeals }} in-flight
              {{ data.activeDeals === 1 ? 'deal will be frozen' : 'deals will be frozen' }}.
            </strong>
            Neither party will be able to move those deals forward, confirm settlement, or halt
            them until the suspension lifts — including the counterparty, who has done nothing
            wrong.
          </span>
        </p>
      }

      <fieldset class="windows">
        <legend class="sk-eyebrow">Suspension window</legend>
        @for (option of options; track option.value) {
          <label class="window" [class.window--on]="duration() === option.value">
            <input
              type="radio"
              name="duration"
              [value]="option.value"
              [checked]="duration() === option.value"
              (change)="duration.set(option.value)"
            />
            <span class="window__body">
              <span class="window__label">{{ option.label }}</span>
              <span class="window__hint">{{ option.hint }}</span>
            </span>
            @if (duration() === option.value) {
              <span class="material-symbols-rounded window__check" aria-hidden="true">
                check_circle
              </span>
            }
          </label>
        }
      </fieldset>

      <div class="sk-field">
        <label for="suspend-notes">Why is this profile being suspended?</label>
        <textarea
          id="suspend-notes"
          class="sk-textarea"
          rows="3"
          maxlength="2000"
          [(ngModel)]="notes"
          [class.is-invalid]="showError"
          placeholder="e.g. Three halts triggered in a month with no explanation given to counterparties."
        ></textarea>
        @if (showError) {
          <p class="sk-error">
            <span class="material-symbols-rounded" aria-hidden="true">error</span>
            Record a reason. Moderation actions are logged against your account and need to be
            explainable later.
          </p>
        }
      </div>
    </div>

    <div mat-dialog-actions class="actions">
      <button type="button" class="sk-btn sk-btn--secondary" (click)="cancel()">Cancel</button>
      <button type="button" class="sk-btn sk-btn--destructive" (click)="submit()">
        <span class="material-symbols-rounded" aria-hidden="true">block</span>
        Suspend profile
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

      .intro {
        margin: 0;
        font-size: 0.9375rem;
      }

      .freeze {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3);
        border-radius: var(--sk-radius);
        background: var(--sk-open-soft);
        color: var(--sk-open-soft-ink);
        box-shadow: inset 0 0 0 1.5px var(--sk-open);
        font-size: 0.875rem;
        line-height: 1.55;
      }

      .freeze .material-symbols-rounded {
        font-size: 1.25rem;
        flex: none;
      }

      .windows {
        display: grid;
        gap: var(--sk-space-2);
        border: 0;
        margin: 0;
        padding: 0;
      }

      .windows legend {
        padding: 0 0 var(--sk-space-1);
      }

      .window {
        position: relative;
        display: flex;
        align-items: center;
        gap: var(--sk-space-2);
        min-height: var(--sk-touch);
        padding: var(--sk-space-2) var(--sk-space-3);
        border: 1px solid var(--sk-line-strong);
        border-radius: var(--sk-radius);
        background: var(--sk-surface);
        cursor: pointer;
      }

      .window:hover {
        background: var(--sk-surface-sunken);
      }

      .window--on {
        border-color: var(--sk-suspended);
        border-width: 2px;
        padding: calc(var(--sk-space-2) - 1px) calc(var(--sk-space-3) - 1px);
        background: var(--sk-suspended-soft);
      }

      .window input {
        position: absolute;
        opacity: 0;
        pointer-events: none;
      }

      .window:has(input:focus-visible) {
        outline: 3px solid var(--sk-accent);
        outline-offset: 2px;
      }

      .window__body {
        display: grid;
        gap: 1px;
        flex: 1 1 auto;
      }

      .window__label {
        font-size: 0.9375rem;
        font-weight: 600;
        color: var(--sk-ink-strong);
      }

      .window__hint {
        font-size: 0.75rem;
        color: var(--sk-ink-muted);
      }

      .window__check {
        font-size: 1.25rem;
        color: var(--sk-suspended);
        font-variation-settings: 'FILL' 1;
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
export class SuspendDialog {
  readonly data = inject<{ name: string; activeDeals: number }>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<SuspendDialog, SuspendDialogResult | null>>(MatDialogRef);

  readonly options = [
    {
      value: SuspensionDuration.OneWeek,
      label: '1 week',
      hint: 'A first, correctable problem',
    },
    {
      value: SuspensionDuration.TwoWeeks,
      label: '2 weeks',
      hint: 'A repeat of something already warned about',
    },
    {
      value: SuspensionDuration.OneMonth,
      label: '1 month',
      hint: 'A serious or repeated pattern',
    },
    {
      value: SuspensionDuration.Permanent,
      label: 'Permanent',
      hint: 'No end date. Consider Remove instead if they should not return at all',
    },
  ];

  readonly duration = signal<SuspensionDuration>(SuspensionDuration.OneWeek);

  notes = '';
  showError = false;

  cancel(): void {
    this.dialogRef.close(null);
  }

  submit(): void {
    const notes = this.notes.trim();

    if (!notes) {
      this.showError = true;
      return;
    }

    this.dialogRef.close({ duration: this.duration(), notes });
  }
}
