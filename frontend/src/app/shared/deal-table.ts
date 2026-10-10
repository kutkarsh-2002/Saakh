import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { DealRow, DealState, ProfileRole } from '../core/models/domain';
import { CATEGORY_META } from '../core/models/status-vocabulary';
import { formatCapacity, formatDate, settlementUrgency } from '../core/util/format';
import { StatusPill } from './status-pill';
import { StarRating } from './star-rating';
import { VerificationBadge } from './verification-badge';

/**
 * The deal table, with the columns the spec specifies: Deal ID, Category,
 * Description (truncated with the full text on hover), Location, Estimated
 * Settlement Time and Status. History adds the rating given and received.
 *
 * On a phone the same rows render as cards rather than a horizontally scrolling
 * table, because this is a mobile-first product and a five-column scroll is
 * unusable one-handed.
 */
@Component({
  selector: 'sk-deal-table',
  imports: [RouterLink, MatTooltipModule, StatusPill, StarRating, VerificationBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Wide screens: the dense back-office table. -->
    <div class="sk-table-wrap table-view">
      <table class="sk-table">
        <caption class="sk-sr-only">{{ caption() }}</caption>
        <thead>
          <tr>
            <th scope="col">Deal</th>
            <th scope="col">Counterparty</th>
            <th scope="col">Category</th>
            <th scope="col">Description</th>
            <th scope="col" class="sk-num">Amount / qty</th>
            <th scope="col">Location</th>
            <th scope="col">Settlement</th>
            <th scope="col">Status</th>
            @if (showRatings()) {
              <th scope="col">Ratings</th>
            }
            <th scope="col"><span class="sk-sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.id) {
            <tr>
              <td class="sk-cell-strong">
                <a [routerLink]="['/deals', row.id]" class="ref">{{ row.reference }}</a>
                <span class="side">{{ sideLabel(row) }}</span>
              </td>

              <td>
                <a [routerLink]="['/counterparties', row.counterparty.id]" class="party">
                  <span class="party__name sk-truncate">{{ row.counterparty.name }}</span>
                  <span class="party__meta">
                    <sk-star-rating
                      [value]="row.counterparty.trust.averageStars ?? 0"
                      [count]="row.counterparty.trust.ratingsReceived"
                    />
                  </span>
                </a>
              </td>

              <td>
                <span class="cat">
                  <span class="material-symbols-rounded" aria-hidden="true">{{
                    categoryMeta[row.category].icon
                  }}</span>
                  {{ row.subType?.displayName ?? categoryMeta[row.category].label }}
                </span>
              </td>

              <!-- Truncated with the full text in a tooltip, per the spec. -->
              <td
                class="sk-cell-clamp"
                [matTooltip]="row.description"
                [attr.title]="row.description"
                matTooltipPosition="above"
              >
                {{ row.description }}
              </td>

              <td class="sk-num">{{ amount(row) }}</td>

              <td>
                <span class="loc">{{ row.district }}</span>
                <span class="loc loc--sub">{{ row.state }}</span>
              </td>

              <td [class]="'settle settle--' + urgency(row)">
                <span class="settle__date">{{ formatDate(row.estimatedSettlementTime) }}</span>
                @if (row.dealState === DealState.Open || row.dealState === DealState.Progress) {
                  <span class="settle__note">{{ settlementNote(row) }}</span>
                }
              </td>

              <td>
                <div class="state">
                  <sk-status-pill [dealState]="row.dealState" />
                  @if (row.iHaltedThisDeal) {
                    <span
                      class="fault"
                      matTooltip="You triggered this halt, so it is recorded against your profile under the v1 rule."
                    >
                      you halted
                    </span>
                  } @else if (row.dealState === DealState.Halted && row.haltedByName) {
                    <span class="fault fault--them">{{ row.haltedByName }} halted</span>
                  } @else if (row.closedOverdue) {
                    <!-- Nobody halted this one: the agreed date passed. -->
                    <span
                      class="fault"
                      matTooltip="The settlement date you both agreed passed without the deal being settled, so the platform closed it. It is recorded on both trust records."
                    >
                      closed overdue
                    </span>
                  }
                  @if (row.frozen) {
                    <span
                      class="frozen"
                      matTooltip="One party's account is restricted by an administrator, so this deal cannot change state."
                    >
                      <span class="material-symbols-rounded" aria-hidden="true">ac_unit</span>
                      frozen
                    </span>
                  }
                </div>
              </td>

              @if (showRatings()) {
                <td>
                  <div class="ratings">
                    <span class="ratings__row">
                      <span class="ratings__label">Given</span>
                      @if (row.ratingGiven) {
                        <span class="ratings__score sk-figure">
                          {{ row.ratingGiven.stars }}
                          <span class="material-symbols-rounded" aria-hidden="true">star</span>
                          <span class="sk-sr-only">out of 5</span>
                        </span>
                      } @else {
                        <span class="ratings__none">not yet</span>
                      }
                    </span>
                    <span class="ratings__row">
                      <span class="ratings__label">Received</span>
                      @if (row.ratingReceived) {
                        <span class="ratings__score sk-figure">
                          {{ row.ratingReceived.stars }}
                          <span class="material-symbols-rounded" aria-hidden="true">star</span>
                          <span class="sk-sr-only">out of 5</span>
                        </span>
                      } @else {
                        <span class="ratings__none">not yet</span>
                      }
                    </span>
                  </div>
                </td>
              }

              <td>
                <div class="row-actions">
                  @if (row.canRate) {
                    <button
                      type="button"
                      class="sk-btn sk-btn--primary sk-btn--sm"
                      (click)="rate.emit(row)"
                    >
                      Rate
                    </button>
                  }
                  @if (row.dealState === DealState.Halted && !row.pendingResumeRequest) {
                    <button
                      type="button"
                      class="sk-btn sk-btn--secondary sk-btn--sm"
                      (click)="resume.emit(row)"
                    >
                      Resume?
                    </button>
                  }
                  @if (row.pendingResumeRequest; as request) {
                    @if (request.requestedByMe) {
                      <span class="pending-note">Waiting on them</span>
                    } @else {
                      <button
                        type="button"
                        class="sk-btn sk-btn--primary sk-btn--sm"
                        (click)="acceptResume.emit(row)"
                      >
                        Agree to resume
                      </button>
                    }
                  }
                  <a class="sk-btn sk-btn--quiet sk-btn--sm" [routerLink]="['/deals', row.id]">
                    Open
                  </a>
                </div>
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>

    <!-- Phones: the same data as cards. -->
    <ul class="card-view" [attr.aria-label]="caption()">
      @for (row of rows(); track row.id) {
        <li class="deal-card">
          <div class="deal-card__top">
            <a [routerLink]="['/deals', row.id]" class="ref">{{ row.reference }}</a>
            <sk-status-pill [dealState]="row.dealState" />
          </div>

          <a [routerLink]="['/counterparties', row.counterparty.id]" class="deal-card__party">
            <span class="deal-card__name">{{ row.counterparty.name }}</span>
            <sk-verification-badge
              [verified]="row.counterparty.gstinVerified"
              [legalName]="row.counterparty.gstinLegalName"
              size="sm"
            />
          </a>

          <p class="deal-card__desc">{{ row.description }}</p>

          <dl class="deal-card__facts">
            <div>
              <dt>Amount / qty</dt>
              <dd class="sk-figure">{{ amount(row) }}</dd>
            </div>
            <div>
              <dt>Settlement</dt>
              <dd [class]="'settle--' + urgency(row)">
                {{ formatDate(row.estimatedSettlementTime) }}
              </dd>
            </div>
            <div>
              <dt>Where</dt>
              <dd>{{ row.district }}, {{ row.state }}</dd>
            </div>
            <div>
              <dt>Your side</dt>
              <dd>{{ sideLabel(row) }}</dd>
            </div>
          </dl>

          @if (row.iHaltedThisDeal) {
            <p class="deal-card__fault">
              <span class="material-symbols-rounded" aria-hidden="true">flag</span>
              You triggered this halt, so it counts against your profile.
            </p>
          } @else if (row.dealState === DealState.Halted && row.haltedByName) {
            <p class="deal-card__fault deal-card__fault--them">
              <span class="material-symbols-rounded" aria-hidden="true">flag</span>
              {{ row.haltedByName }} halted this deal.
            </p>
          }

          <div class="deal-card__actions">
            @if (row.canRate) {
              <button type="button" class="sk-btn sk-btn--primary sk-btn--sm" (click)="rate.emit(row)">
                Rate
              </button>
            }
            @if (row.dealState === DealState.Halted && !row.pendingResumeRequest) {
              <button
                type="button"
                class="sk-btn sk-btn--secondary sk-btn--sm"
                (click)="resume.emit(row)"
              >
                Resume?
              </button>
            }
            @if (row.pendingResumeRequest; as request) {
              @if (!request.requestedByMe) {
                <button
                  type="button"
                  class="sk-btn sk-btn--primary sk-btn--sm"
                  (click)="acceptResume.emit(row)"
                >
                  Agree to resume
                </button>
              } @else {
                <span class="pending-note">Waiting on them</span>
              }
            }
            <a class="sk-btn sk-btn--secondary sk-btn--sm" [routerLink]="['/deals', row.id]">
              Open deal
            </a>
          </div>
        </li>
      }
    </ul>
  `,
  styleUrl: './deal-table.scss',
})
export class DealTable {
  readonly rows = input.required<DealRow[]>();
  readonly showRatings = input(false);
  readonly caption = input('Deals');

  readonly rate = output<DealRow>();
  readonly resume = output<DealRow>();
  readonly acceptResume = output<DealRow>();

  readonly DealState = DealState;
  readonly categoryMeta = CATEGORY_META;
  readonly formatDate = formatDate;

  amount(row: DealRow): string {
    return formatCapacity(row.capacity, row.capacityUnit, row.category);
  }

  sideLabel(row: DealRow): string {
    return row.mySide === ProfileRole.Lender ? 'You lend' : 'You seek';
  }

  urgency(row: DealRow): 'overdue' | 'soon' | 'ok' {
    // Only an in-flight deal has an urgent settlement date; a closed one does not.
    if (row.dealState === DealState.Completed || row.dealState === DealState.Halted) {
      return 'ok';
    }
    return settlementUrgency(row.estimatedSettlementTime);
  }

  settlementNote(row: DealRow): string {
    const days = Math.ceil(
      (new Date(row.estimatedSettlementTime).getTime() - Date.now()) / 86400000,
    );

    if (days < 0) {
      return `${Math.abs(days)}d overdue`;
    }
    if (days === 0) {
      return 'due today';
    }
    return `in ${days}d`;
  }
}
