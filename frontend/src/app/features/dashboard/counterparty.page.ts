import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router, RouterLink } from '@angular/router';
import { SaakhApi } from '../../core/api/saakh.api';
import { ProfileSummary, Rating } from '../../core/models/domain';
import { BUSINESS_SIZE_LABEL, CATEGORY_META, ROLE_META } from '../../core/models/status-vocabulary';
import { formatDate, formatRange } from '../../core/util/format';
import { ToastService } from '../../core/util/toast.service';
import { LoadingBlock, PageHeader } from '../../shared/ui';
import { StarRating } from '../../shared/star-rating';
import { StatusPill } from '../../shared/status-pill';
import { TrustSummaryView } from '../../shared/trust-summary';
import { VerificationBadge } from '../../shared/verification-badge';
import { SendInterestDialog, SendInterestDialogData } from './send-interest.dialog';

/**
 * A counterparty's trust record: the portable record this product exists to
 * create, read by someone deciding whether to extend credit to a stranger.
 *
 * It leads with verified identity and the settled history, and it shows the
 * individual ratings with their notes, because "11 of 12 settled clean" means
 * more when you can read what the counterparties actually said.
 */
@Component({
  selector: 'sk-counterparty-page',
  imports: [
    RouterLink,
    PageHeader,
    LoadingBlock,
    StarRating,
    StatusPill,
    TrustSummaryView,
    VerificationBadge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sk-page">
      <nav class="crumbs" aria-label="Breadcrumb">
        <a routerLink="/dashboard">
          <span class="material-symbols-rounded" aria-hidden="true">arrow_back</span>
          Opportunity
        </a>
      </nav>

      @if (loading()) {
        <div class="sk-card"><sk-loading [count]="6" label="Loading the trust record" /></div>
      } @else if (profile(); as party) {
        <sk-page-header
          [eyebrow]="roleMeta[party.role].label + ' · ' + sizeLabel[party.businessSize]"
          [title]="party.name"
          [subtitle]="party.district + ', ' + party.state + ', ' + party.country"
        >
          <button type="button" class="sk-btn sk-btn--primary" (click)="sendInterest(party)">
            <span class="material-symbols-rounded" aria-hidden="true">send</span>
            Send interest
          </button>
        </sk-page-header>

        <div class="badges">
          <sk-verification-badge
            [verified]="party.gstinVerified"
            [legalName]="party.gstinLegalName"
            [gstin]="party.gstinMasked"
            [showUnverified]="!party.gstinVerified"
          />
          <sk-status-pill [verification]="party.verificationStatus" />
          <sk-status-pill [availability]="party.availabilityStatus" />
        </div>

        <div class="layout">
          <div class="col">
            <section class="sk-card sk-card-pad">
              <sk-trust-summary [trust]="party.trust" />
            </section>

            <section class="sk-card sk-card-pad">
              <h2>Details</h2>
              <dl class="facts">
                <div class="facts__row">
                  <dt>{{ party.role === 1 ? 'Provides' : 'Needs' }}</dt>
                  <dd>
                    <span class="cat">
                      <span class="material-symbols-rounded" aria-hidden="true">{{
                        categoryMeta[party.category].icon
                      }}</span>
                      {{ party.subType?.displayName ?? categoryMeta[party.category].label }}
                    </span>
                  </dd>
                </div>
                <div class="facts__row">
                  <dt>Capacity</dt>
                  <dd class="sk-figure">{{ capacity() }}</dd>
                </div>
                @if (party.ownTradeDescription) {
                  <div class="facts__row">
                    <dt>Own trade</dt>
                    <dd>{{ party.ownTradeDescription }}</dd>
                  </div>
                }
                @if (party.gstinLegalName) {
                  <div class="facts__row">
                    <dt>Registered as</dt>
                    <dd>{{ party.gstinLegalName }}</dd>
                  </div>
                }
                @if (party.gstinMasked) {
                  <div class="facts__row">
                    <dt>GSTIN</dt>
                    <dd class="gstin">{{ party.gstinMasked }}</dd>
                  </div>
                }
                <div class="facts__row">
                  <dt>On Saakh since</dt>
                  <dd>{{ formatDate(party.createdAt) }}</dd>
                </div>
              </dl>
            </section>
          </div>

          <div class="col">
            <section class="sk-card sk-card-pad">
              <h2>What counterparties said</h2>
              <p class="sk-meta ratings__intro">
                Every rating is tied to one settled or halted deal. Nothing here is an average of
                averages.
              </p>

              @if (ratings().length === 0) {
                <p class="ratings__none">
                  No ratings yet. This profile has not had a deal reach Completed or Halted, so
                  there is nothing to show — not a low score, simply no history.
                </p>
              } @else {
                <ul class="ratings">
                  @for (rating of ratings(); track rating.id) {
                    <li class="rating">
                      <div class="rating__head">
                        <sk-star-rating [value]="rating.stars" />
                        <span class="rating__who">{{ rating.raterName }}</span>
                        <span class="rating__when sk-meta">{{ formatDate(rating.createdAt) }}</span>
                      </div>
                      @if (rating.comment) {
                        <p class="rating__note">{{ rating.comment }}</p>
                      }
                    </li>
                  }
                </ul>
              }
            </section>
          </div>
        </div>
      }
    </div>
  `,
  styleUrl: './counterparty.page.scss',
})
export class CounterpartyPage {
  readonly id = input.required<string>();

  private readonly api = inject(SaakhApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly categoryMeta = CATEGORY_META;
  readonly roleMeta = ROLE_META;
  readonly sizeLabel = BUSINESS_SIZE_LABEL;
  readonly formatDate = formatDate;

  readonly loading = signal(true);
  readonly profile = signal<ProfileSummary | null>(null);
  readonly ratings = signal<Rating[]>([]);

  readonly capacity = computed(() => {
    const party = this.profile();
    return party
      ? formatRange(party.capacityMin, party.capacityMax, party.capacityUnit, party.category)
      : '';
  });

  constructor() {
    queueMicrotask(() => this.load());
  }

  private load(): void {
    this.loading.set(true);

    this.api.profile(this.id()).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.error(error);
        void this.router.navigate(['/dashboard']);
      },
    });

    this.api.profileRatings(this.id()).subscribe({
      next: (ratings) => this.ratings.set(ratings),
      error: () => undefined,
    });
  }

  sendInterest(party: ProfileSummary): void {
    const data: SendInterestDialogData = { profile: party };

    this.dialog
      .open(SendInterestDialog, { data, width: '520px', maxWidth: '94vw' })
      .afterClosed()
      .subscribe((note: string | null | undefined) => {
        if (note === undefined) {
          return;
        }

        this.api.sendInterest(party.id, note).subscribe({
          next: () =>
            this.toast.success(`Interest sent to ${party.name}. Chat opens once they accept.`),
          error: (error: unknown) => this.toast.error(error),
        });
      });
  }
}
