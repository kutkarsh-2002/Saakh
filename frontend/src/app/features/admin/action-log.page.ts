import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { AdminActionLog, AdminActionType, SuspensionDuration } from '../../core/models/domain';
import { formatDateTime, formatRelative } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { EmptyState, FilterChip, LoadingBlock } from '../../shared/ui';

/**
 * The platform-wide action log: every verification decision and moderation
 * action, with the admin who took it and when.
 *
 * This exists because the spec requires admin actions to be auditable, and an
 * audit trail nobody can read is not an audit trail.
 */
@Component({
  selector: 'sk-action-log-page',
  imports: [RouterLink, FilterChip, EmptyState, LoadingBlock],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sk-card">
      <div class="sk-card-head">
        <div>
          <h2>Action log</h2>
          <p class="sk-meta">
            Every verification decision and moderation action across the platform, newest first.
          </p>
        </div>
        <span class="sk-spacer"></span>
        <div class="chips" role="group" aria-label="Filter by action type">
          <sk-filter-chip label="All" [selected]="filter() === null" (toggled)="setFilter(null)" />
          <sk-filter-chip
            label="Verification"
            icon="fact_check"
            [selected]="filter() === 'verification'"
            (toggled)="setFilter('verification')"
          />
          <sk-filter-chip
            label="Moderation"
            icon="gavel"
            [selected]="filter() === 'moderation'"
            (toggled)="setFilter('moderation')"
          />
        </div>
      </div>

      @if (loading()) {
        <sk-loading [count]="8" label="Loading the action log" />
      } @else if (rows().length === 0) {
        <sk-empty-state
          icon="receipt_long"
          title="Nothing logged yet"
          message="Verification decisions and moderation actions appear here as soon as they are taken."
        />
      } @else {
        <div class="sk-table-wrap log-table">
          <table class="sk-table">
            <caption class="sk-sr-only">Admin action log</caption>
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">Action</th>
                <th scope="col">Target profile</th>
                <th scope="col">Admin</th>
                <th scope="col">Notes</th>
              </tr>
            </thead>
            <tbody>
              @for (entry of rows(); track entry.id) {
                <tr>
                  <td>
                    <span class="when">{{ formatDateTime(entry.occurredAt) }}</span>
                    <span class="sub">{{ formatRelative(entry.occurredAt) }}</span>
                  </td>
                  <td>
                    <span
                      class="action"
                      [style.--action-color]="'var(--sk-' + tone(entry.actionType) + ')'"
                      [style.--action-bg]="'var(--sk-' + tone(entry.actionType) + '-soft)'"
                      [style.--action-ink]="'var(--sk-' + tone(entry.actionType) + '-soft-ink)'"
                    >
                      <span class="material-symbols-rounded" aria-hidden="true">{{
                        icon(entry.actionType)
                      }}</span>
                      {{ labels[entry.actionType] }}
                    </span>
                    @if (entry.suspensionDuration) {
                      <span class="sub">{{ durations[entry.suspensionDuration] }}</span>
                    }
                  </td>
                  <td>
                    <a
                      class="target"
                      [routerLink]="['/admin/directory', entry.targetProfileId]"
                      >{{ entry.targetProfileName }}</a
                    >
                  </td>
                  <td>{{ entry.adminName }}</td>
                  <td class="sk-cell-clamp" [attr.title]="entry.notes">{{ entry.notes ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <ul class="log-cards">
          @for (entry of rows(); track entry.id) {
            <li class="log-card">
              <div class="log-card__top">
                <span
                  class="action"
                  [style.--action-color]="'var(--sk-' + tone(entry.actionType) + ')'"
                  [style.--action-bg]="'var(--sk-' + tone(entry.actionType) + '-soft)'"
                  [style.--action-ink]="'var(--sk-' + tone(entry.actionType) + '-soft-ink)'"
                >
                  <span class="material-symbols-rounded" aria-hidden="true">{{
                    icon(entry.actionType)
                  }}</span>
                  {{ labels[entry.actionType] }}
                </span>
                <span class="sub">{{ formatRelative(entry.occurredAt) }}</span>
              </div>
              <a class="target" [routerLink]="['/admin/directory', entry.targetProfileId]">
                {{ entry.targetProfileName }}
              </a>
              @if (entry.notes) {
                <p class="log-card__notes">{{ entry.notes }}</p>
              }
              <p class="sub">
                {{ entry.adminName }} · {{ formatDateTime(entry.occurredAt) }}
                @if (entry.suspensionDuration) {
                  · {{ durations[entry.suspensionDuration] }}
                }
              </p>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styleUrl: './action-log.page.scss',
})
export class ActionLogPage {
  private readonly api = inject(SaakhApi);
  private readonly toast = inject(ToastService);

  readonly formatDateTime = formatDateTime;
  readonly formatRelative = formatRelative;

  readonly loading = signal(true);
  readonly all = signal<AdminActionLog[]>([]);
  readonly filter = signal<'verification' | 'moderation' | null>(null);

  readonly labels: Record<AdminActionType, string> = {
    [AdminActionType.Warning]: 'Warning',
    [AdminActionType.Suspend]: 'Suspended',
    [AdminActionType.Remove]: 'Removed',
    [AdminActionType.VerificationApproved]: 'Approved',
    [AdminActionType.VerificationRejected]: 'Rejected',
    [AdminActionType.SuspensionLifted]: 'Suspension lifted',
  };

  readonly durations: Record<SuspensionDuration, string> = {
    [SuspensionDuration.OneWeek]: '1 week',
    [SuspensionDuration.TwoWeeks]: '2 weeks',
    [SuspensionDuration.OneMonth]: '1 month',
    [SuspensionDuration.Permanent]: 'Permanent',
  };

  readonly rows = computed(() => {
    const filter = this.filter();
    if (filter === null) {
      return this.all();
    }

    const verificationActions = [
      AdminActionType.VerificationApproved,
      AdminActionType.VerificationRejected,
    ];

    return this.all().filter((entry) =>
      filter === 'verification'
        ? verificationActions.includes(entry.actionType)
        : !verificationActions.includes(entry.actionType),
    );
  });

  constructor() {
    this.load();
  }

  setFilter(filter: 'verification' | 'moderation' | null): void {
    this.filter.set(filter);
  }

  /** Reuses the status tokens so an action's colour matches its outcome elsewhere. */
  tone(action: AdminActionType): string {
    switch (action) {
      case AdminActionType.VerificationApproved:
      case AdminActionType.SuspensionLifted:
        return 'completed';
      case AdminActionType.VerificationRejected:
        return 'rejected';
      case AdminActionType.Warning:
        return 'needs-approval';
      case AdminActionType.Suspend:
        return 'suspended';
      case AdminActionType.Remove:
        return 'removed';
      default:
        return 'inactive';
    }
  }

  icon(action: AdminActionType): string {
    switch (action) {
      case AdminActionType.VerificationApproved:
        return 'how_to_reg';
      case AdminActionType.VerificationRejected:
        return 'cancel';
      case AdminActionType.Warning:
        return 'campaign';
      case AdminActionType.Suspend:
        return 'block';
      case AdminActionType.Remove:
        return 'person_off';
      case AdminActionType.SuspensionLifted:
        return 'lock_open';
      default:
        return 'history';
    }
  }

  private load(): void {
    this.loading.set(true);
    this.api.actionLog().subscribe({
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
}
