import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import {
  AdminActionType,
  AdminProfileDetail,
  AvailabilityStatus,
  DealState,
  EvidenceDecision,
  SuspensionDuration,
  VerificationStatus,
} from '../../core/models/domain';
import { BUSINESS_SIZE_LABEL, CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { formatCapacity, formatDate, formatDateTime, formatRange } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { ConfirmDialog, ConfirmDialogData, ConfirmDialogResult } from '../../shared/confirm-dialog';
import { LoadingBlock } from '../../shared/ui';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';
import { SuspendDialog, SuspendDialogResult } from './suspend.dialog';

/**
 * One profile, from the moderator's side: identity, evidence, every deal it has
 * been part of, the full action log against it, and the four moderation actions.
 *
 * Each action states its real effect before it is taken, including which of
 * them freeze the profile's in-flight deals.
 */
@Component({
  selector: 'sk-admin-profile-page',
  imports: [
    RouterLink,
    LoadingBlock,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-profile.page.html',
  styleUrl: './admin-profile.page.scss',
})
export class AdminProfilePage {
  readonly id = input.required<string>();

  private readonly api = inject(SaakhApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly VerificationStatus = VerificationStatus;
  readonly AvailabilityStatus = AvailabilityStatus;
  readonly EvidenceDecision = EvidenceDecision;
  readonly AdminActionType = AdminActionType;
  readonly DealState = DealState;
  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly sizeLabel = BUSINESS_SIZE_LABEL;
  readonly formatDate = formatDate;
  readonly formatDateTime = formatDateTime;

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly detail = signal<AdminProfileDetail | null>(null);

  readonly profile = computed(() => this.detail()?.profile ?? null);

  readonly capacity = computed(() => {
    const profile = this.profile();
    return profile
      ? formatRange(profile.capacityMin, profile.capacityMax, profile.capacityUnit, profile.category)
      : '';
  });

  readonly activeDeals = computed(
    () =>
      this.detail()?.deals.filter(
        (deal) => deal.dealState === DealState.Open || deal.dealState === DealState.Progress,
      ) ?? [],
  );

  readonly actionLabels: Record<AdminActionType, string> = {
    [AdminActionType.Warning]: 'Warning recorded',
    [AdminActionType.Suspend]: 'Suspended',
    [AdminActionType.Remove]: 'Removed',
    [AdminActionType.VerificationApproved]: 'Verification approved',
    [AdminActionType.VerificationRejected]: 'Verification rejected',
    [AdminActionType.SuspensionLifted]: 'Suspension lifted',
  };

  readonly durationLabels: Record<SuspensionDuration, string> = {
    [SuspensionDuration.OneWeek]: '1 week',
    [SuspensionDuration.TwoWeeks]: '2 weeks',
    [SuspensionDuration.OneMonth]: '1 month',
    [SuspensionDuration.Permanent]: 'Permanent',
  };

  constructor() {
    queueMicrotask(() => this.load());
  }

  private load(): void {
    this.loading.set(true);

    this.api.adminProfile(this.id()).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
        void this.router.navigate(['/admin/directory']);
      },
    });
  }

  dealAmount(capacity: number, unit: string, category: number): string {
    return formatCapacity(capacity, unit, category);
  }

  evidenceUrl(id: string): string {
    return this.api.evidenceUrl(id);
  }

  // ---- verification --------------------------------------------------------

  approve(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    const data: ConfirmDialogData = {
      title: `Approve ${profile.name}?`,
      message:
        'Their account opens immediately: every tab unlocks and they appear in search.',
      consequence: 'The approval email is queued and this decision is logged against you.',
      confirmLabel: 'Approve account',
    };

    this.confirm(data, () => {
      this.api.reviewVerification(profile.id, true, null).subscribe({
        next: () => {
          this.toast.success(`${profile.name} is approved.`);
          this.load();
        },
        error: (error: unknown) => this.toast.error(error),
      });
    });
  }

  reject(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    const data: ConfirmDialogData = {
      title: `Reject ${profile.name}'s documents?`,
      message: 'Their account stays locked, and they can resubmit new documents.',
      consequence: 'Your reason is shown to them and is the only guidance they get.',
      confirmLabel: 'Reject documents',
      destructive: true,
      reasonLabel: 'Reason shown to the user',
      reasonRequired: true,
    };

    this.confirm(data, (reason) => {
      this.api.reviewVerification(profile.id, false, reason).subscribe({
        next: () => {
          this.toast.success(`${profile.name}'s documents were rejected.`);
          this.load();
        },
        error: (error: unknown) => this.toast.error(error),
      });
    });
  }

  // ---- moderation ----------------------------------------------------------

  warn(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    const data: ConfirmDialogData = {
      title: `Record a warning against ${profile.name}?`,
      message: 'A warning changes nothing about their access. It is a note for other admins.',
      consequence:
        'Warnings are visible to admins only and are never shown publicly on the profile.',
      confirmLabel: 'Record warning',
      reasonLabel: 'What is the warning for?',
      reasonRequired: true,
    };

    this.confirm(data, (reason) => this.moderate(AdminActionType.Warning, null, reason));
  }

  suspend(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    this.dialog
      .open<SuspendDialog, { name: string; activeDeals: number }, SuspendDialogResult>(
        SuspendDialog,
        {
          data: { name: profile.name, activeDeals: this.activeDeals().length },
          width: '560px',
          maxWidth: '94vw',
        },
      )
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.moderate(AdminActionType.Suspend, result.duration, result.notes);
        }
      });
  }

  liftSuspension(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    const data: ConfirmDialogData = {
      title: `Lift the suspension on ${profile.name}?`,
      message: 'They return to search immediately and their frozen deals can move again.',
      confirmLabel: 'Lift suspension',
    };

    this.confirm(data, (reason) => this.moderate(AdminActionType.SuspensionLifted, null, reason));
  }

  remove(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }

    const data: ConfirmDialogData = {
      title: `Remove ${profile.name} from the platform?`,
      message:
        'The profile is deactivated entirely. They can no longer act on Saakh at all, and this is not reversible from the console.',
      consequence: `Their deal history is retained for audit, including ${this.activeDeals().length} in-flight ${
        this.activeDeals().length === 1 ? 'deal that is' : 'deals that are'
      } frozen by this. Use Suspend instead if you want a time-bound restriction.`,
      confirmLabel: 'Remove profile',
      destructive: true,
      reasonLabel: 'Why is this profile being removed?',
      reasonRequired: true,
    };

    this.confirm(data, (reason) => this.moderate(AdminActionType.Remove, null, reason));
  }

  private confirm(data: ConfirmDialogData, run: (reason: string | null) => void): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, ConfirmDialogResult>(ConfirmDialog, {
        data,
        width: '560px',
        maxWidth: '94vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result?.confirmed) {
          run(result.reason);
        }
      });
  }

  private moderate(
    action: AdminActionType,
    duration: SuspensionDuration | null,
    notes: string | null,
  ): void {
    this.busy.set(true);

    this.api.moderate(this.id(), action, duration, notes).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(`${this.actionLabels[action]}.`);
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.error(error);
      },
    });
  }
}
