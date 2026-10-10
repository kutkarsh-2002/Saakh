import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SaakhApi } from '../../core/api/saakh.api';
import { EvidenceViewer } from '../../core/util/evidence-viewer';
import {
  EvidenceDocument,
  EvidenceDecision,
  VerificationQueueRow,
  VerificationStatus,
} from '../../core/models/domain';
import { CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { formatDateTime, formatRange } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { ConfirmDialog, ConfirmDialogData, ConfirmDialogResult } from '../../shared/confirm-dialog';
import { EmptyState, FilterChip, LoadingBlock } from '../../shared/ui';
import { StatusPill } from '../../shared/status-pill';

type QueueFilter = 'pending' | 'needs' | 'rejected' | 'all';

/**
 * The verification queue: the human checkpoint for accounts the platform cannot
 * verify automatically.
 *
 * Pending rows come first and carry the hours they have been waiting, because
 * a slow review stalls a vendor's onboarding entirely — they are locked out of
 * every feature until this decision is made.
 */
@Component({
  selector: 'sk-verification-queue-page',
  imports: [
    RouterLink,
    MatTooltipModule,
    FilterChip,
    EmptyState,
    LoadingBlock,
    StatusPill,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './verification-queue.page.html',
  styleUrl: './verification-queue.page.scss',
})
export class VerificationQueuePage {
  private readonly api = inject(SaakhApi);
  private readonly evidenceViewer = inject(EvidenceViewer);
  private readonly realtime = inject(RealtimeService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  readonly VerificationStatus = VerificationStatus;
  readonly EvidenceDecision = EvidenceDecision;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly formatDateTime = formatDateTime;

  readonly loading = signal(true);
  readonly all = signal<VerificationQueueRow[]>([]);
  readonly filter = signal<QueueFilter>('pending');
  readonly busyId = signal<string | null>(null);

  /** 48 hours is the target turnaround this build commits to in the UI. */
  private readonly slaHours = 48;

  readonly pending = computed(() =>
    this.all().filter((row) => row.profile.verificationStatus === VerificationStatus.Pending),
  );

  readonly needsApproval = computed(() =>
    this.all().filter((row) => row.profile.verificationStatus === VerificationStatus.NeedsApproval),
  );

  readonly rejected = computed(() =>
    this.all().filter((row) => row.profile.verificationStatus === VerificationStatus.Rejected),
  );

  readonly breached = computed(
    () => this.pending().filter((row) => (row.hoursWaiting ?? 0) > this.slaHours).length,
  );

  readonly rows = computed(() => {
    switch (this.filter()) {
      case 'pending':
        return this.pending();
      case 'needs':
        return this.needsApproval();
      case 'rejected':
        return this.rejected();
      default:
        return this.all();
    }
  });

  constructor() {
    this.load();
    this.realtime.verification$.subscribe(() => this.load());
  }

  private load(): void {
    this.loading.set(true);
    this.api.verificationQueue().subscribe({
      next: (rows) => {
        this.all.set(rows);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
      },
    });
  }

  setFilter(filter: QueueFilter): void {
    this.filter.set(filter);
  }

  capacity(row: VerificationQueueRow): string {
    return formatRange(
      row.profile.capacityMin,
      row.profile.capacityMax,
      row.profile.capacityUnit,
      row.profile.category,
    );
  }

  waitingLabel(row: VerificationQueueRow): string {
    const hours = row.hoursWaiting;
    if (hours === null) {
      return 'Nothing submitted';
    }
    if (hours < 1) {
      return 'Under an hour';
    }
    if (hours < 48) {
      return `${Math.round(hours)}h waiting`;
    }
    return `${Math.floor(hours / 24)}d waiting`;
  }

  isBreached(row: VerificationQueueRow): boolean {
    return (row.hoursWaiting ?? 0) > this.slaHours;
  }

  openEvidence(document: EvidenceDocument): void {
    this.evidenceViewer.open(document);
  }

  approve(row: VerificationQueueRow): void {
    const data: ConfirmDialogData = {
      title: `Approve ${row.profile.name}?`,
      message:
        'Their account opens immediately: every tab unlocks, they appear in search, and they can send and receive interest.',
      consequence:
        'The approval email goes out straight away, and this decision is logged against your admin account with a timestamp.',
      confirmLabel: 'Approve account',
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '540px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result?.confirmed) {
          this.submit(row, true, null);
        }
      });
  }

  reject(row: VerificationQueueRow): void {
    const data: ConfirmDialogData = {
      title: `Reject ${row.profile.name}'s documents?`,
      message:
        'Their account stays locked. They see your reason and can upload new documents, which puts them straight back in this queue.',
      consequence:
        'Write the reason as an instruction, not a verdict: this is the only guidance they get on what to send instead.',
      confirmLabel: 'Reject documents',
      destructive: true,
      reasonLabel: 'Reason shown to the user',
      reasonPlaceholder:
        'e.g. The shop licence photo is too dark to read the name. Please re-take it in daylight.',
      reasonRequired: true,
    };

    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '560px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result?.confirmed) {
          this.submit(row, false, result.reason);
        }
      });
  }

  private submit(row: VerificationQueueRow, approve: boolean, reason: string | null): void {
    this.busyId.set(row.profile.id);

    this.api.reviewVerification(row.profile.id, approve, reason).subscribe({
      next: () => {
        this.busyId.set(null);
        this.toast.success(
          approve
            ? `${row.profile.name} is approved. The approval email has been queued.`
            : `${row.profile.name}'s documents were rejected and they have been told why.`,
        );
        this.load();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.toast.error(error);
      },
    });
  }
}
