import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { SaakhApi } from '../../core/api/saakh.api';
import { DealRow } from '../../core/models/domain';
import { ToastService } from '../../core/util/toast.service';
import {
  ConfirmDialog,
  ConfirmDialogData,
  ConfirmDialogResult,
} from '../../shared/confirm-dialog';
import { RateDealDialog, RateDealDialogData, RateDealResult } from './rate-deal.dialog';

/**
 * The deal actions that appear in more than one place — rating, halting, and
 * the two halves of a resume agreement — so the dashboard, the open-deals
 * table, the history table and the workspace all state the consequences in the
 * same words and cannot drift apart.
 */
@Injectable({ providedIn: 'root' })
export class DealActions {
  private readonly api = inject(SaakhApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  rate(deal: DealRow, onDone: () => void): void {
    const data: RateDealDialogData = { deal };

    this.dialog
      .open<RateDealDialog, RateDealDialogData, RateDealResult>(RateDealDialog, {
        data,
        width: '520px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }

        this.api.rateDeal(deal.id, result.stars, result.comment).subscribe({
          next: () => {
            this.toast.success(
              `You rated ${deal.counterparty.name} ${result.stars} of 5. It is now on their trust record.`,
            );
            onDone();
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }

  halt(deal: DealRow, onDone: () => void): void {
    const data: ConfirmDialogData = {
      title: `Halt deal ${deal.reference}?`,
      message: `This stops the arrangement with ${deal.counterparty.name}. The deal does not resolve on its own — it stays stalled until you both agree to resume it.`,
      // The v1 fault rule is stated plainly before the click, not after.
      consequence:
        'Because you are the one triggering the halt, this deal is recorded as at fault against your profile, and it will count against your rating. If the other side has stopped responding, consider messaging them first.',
      confirmLabel: 'Halt this deal',
      destructive: true,
      reasonLabel: 'Why are you halting it?',
      reasonPlaceholder: 'e.g. The first consignment never arrived and calls went unanswered.',
      reasonRequired: false,
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '540px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result?.confirmed) {
          return;
        }

        this.api.haltDeal(deal.id, result.reason).subscribe({
          next: () => {
            this.toast.info(`Deal ${deal.reference} is halted. You can rate it now.`);
            onDone();
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }

  requestResume(deal: DealRow, onDone: () => void): void {
    const data: ConfirmDialogData = {
      title: `Ask to resume ${deal.reference}?`,
      message: `${deal.counterparty.name} will be asked whether they want to pick this deal back up.`,
      consequence:
        'A one-sided request changes nothing by itself. The deal only returns to Progress once they accept.',
      confirmLabel: 'Send resume request',
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '520px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result?.confirmed) {
          return;
        }

        this.api.requestResume(deal.id).subscribe({
          next: () => {
            this.toast.success(`Resume request sent to ${deal.counterparty.name}.`);
            onDone();
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }

  acceptResume(deal: DealRow, onDone: () => void): void {
    const request = deal.pendingResumeRequest;

    if (!request) {
      return;
    }

    const data: ConfirmDialogData = {
      title: `Resume ${deal.reference}?`,
      message: `${deal.counterparty.name} asked to pick this deal back up.`,
      consequence:
        'Accepting moves the deal straight back to Progress and returns it to your open deals.',
      confirmLabel: 'Agree to resume',
      cancelLabel: 'Not now',
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '520px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result?.confirmed) {
          return;
        }

        this.api.respondToResume(deal.id, request.id, true).subscribe({
          next: () => {
            this.toast.success(`Deal ${deal.reference} is back in progress.`);
            onDone();
          },
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }
}
