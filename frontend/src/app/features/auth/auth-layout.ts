import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Shared frame for sign-in and sign-up.
 *
 * The left panel states what the product does, in the vendor's own terms, with
 * the three stages of a deal spelled out. For a user deciding whether to trust
 * a new platform with their trading record, that explanation is the work, not
 * decoration, so it is given real space on a wide screen and collapses to a
 * single line on a phone.
 */
@Component({
  selector: 'sk-auth-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <aside class="pitch">
        <div class="pitch__inner">
          <div class="pitch__brand">
            <span class="pitch__mark" aria-hidden="true">
              <svg viewBox="0 0 32 32" width="34" height="34">
                <path
                  d="M16 3.5l11 4.8v7.4c0 6.6-4.4 12.1-11 13.8-6.6-1.7-11-7.2-11-13.8V8.3l11-4.8z"
                  fill="currentColor"
                />
                <path
                  d="M10.8 16.2l3.7 3.8 6.7-7.5"
                  fill="none"
                  stroke="var(--sk-brand)"
                  stroke-width="2.6"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                />
              </svg>
            </span>
            <span class="pitch__name">Saakh</span>
          </div>

          <h2 class="pitch__headline">
            Years of reliable trading, in a record a new partner can actually check.
          </h2>

          <p class="pitch__sub">
            Your track record with the suppliers and financiers you already know is worth
            something. Saakh makes it portable, so a counterparty meeting you for the first time
            can see it.
          </p>

          <ol class="steps">
            <li class="steps__item">
              <span class="steps__num sk-figure">1</span>
              <span>
                <strong>Find each other.</strong>
                Lenders see Seekers, Seekers see Lenders, ranked by location, what you deal in, and
                settled history.
              </span>
            </li>
            <li class="steps__item">
              <span class="steps__num sk-figure">2</span>
              <span>
                <strong>Agree the terms.</strong>
                Send interest, negotiate in chat, then raise a ticket that puts the arrangement on
                the record.
              </span>
            </li>
            <li class="steps__item">
              <span class="steps__num sk-figure">3</span>
              <span>
                <strong>Settle and be rated.</strong>
                Every deal that closes adds to your trust record and moves you up for the next
                counterparty.
              </span>
            </li>
          </ol>

          <p class="pitch__note">
            <span class="material-symbols-rounded" aria-hidden="true">info</span>
            Saakh never holds, moves or guarantees your money or material. The transfer stays
            between you and your counterparty; this is the record and the trust layer around it.
          </p>
        </div>
      </aside>

      <section class="form">
        <div class="form__inner">
          <header class="form__head">
            <h1>{{ title() }}</h1>
            @if (subtitle()) {
              <p class="sk-lede">{{ subtitle() }}</p>
            }
          </header>

          <ng-content />
        </div>
      </section>
    </div>
  `,
  styles: [
    `
      .shell {
        display: grid;
        min-height: 100vh;
        grid-template-columns: 1fr;
      }

      @media (min-width: 1000px) {
        .shell {
          /* The pitch is the smaller half: the form is what the user came for. */
          grid-template-columns: 0.95fr 1.05fr;
        }
      }

      .pitch {
        background: var(--sk-brand);
        color: var(--sk-ink-inverse);
        padding: var(--sk-space-5) var(--sk-space-4);
      }

      @media (min-width: 1000px) {
        .pitch {
          display: grid;
          align-items: center;
          padding: var(--sk-space-8) var(--sk-space-7);
        }
      }

      .pitch__inner {
        max-width: 34rem;
        margin: 0 auto;
      }

      .pitch__brand {
        display: flex;
        align-items: center;
        gap: var(--sk-space-2);
        color: var(--sk-ink-inverse);
      }

      .pitch__mark {
        display: grid;
        place-items: center;
      }

      .pitch__name {
        font-family: var(--sk-font-display);
        font-size: 1.5rem;
        font-weight: 700;
        letter-spacing: -0.015em;
      }

      .pitch__headline {
        font-family: var(--sk-font-display);
        font-size: clamp(1.375rem, 1.1rem + 1.5vw, 2.125rem);
        font-weight: 600;
        line-height: 1.2;
        letter-spacing: -0.015em;
        color: var(--sk-ink-inverse);
        margin: var(--sk-space-5) 0 var(--sk-space-3);
      }

      .pitch__sub {
        margin: 0;
        font-size: 1rem;
        line-height: 1.6;
        /* Warmer than pure white at body size, and still well past 4.5:1. */
        color: #dceae3;
      }

      .steps {
        list-style: none;
        margin: var(--sk-space-5) 0;
        padding: 0;
        display: grid;
        gap: var(--sk-space-3);
      }

      .steps__item {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--sk-space-3);
        font-size: 0.9375rem;
        line-height: 1.55;
        color: #dceae3;
      }

      .steps__item strong {
        color: var(--sk-ink-inverse);
        display: block;
      }

      .steps__num {
        display: grid;
        place-items: center;
        width: 28px;
        height: 28px;
        border-radius: 50%;
        background: rgb(255 255 255 / 15%);
        color: var(--sk-ink-inverse);
        font-size: 0.875rem;
        font-weight: 700;
      }

      .pitch__note {
        display: flex;
        gap: var(--sk-space-2);
        margin: 0;
        padding: var(--sk-space-3);
        border-radius: var(--sk-radius);
        background: rgb(0 0 0 / 18%);
        font-size: 0.8125rem;
        line-height: 1.55;
        color: #d2e3da;
      }

      .pitch__note .material-symbols-rounded {
        font-size: 1.125rem;
        flex: none;
      }

      .form {
        display: grid;
        align-items: start;
        padding: var(--sk-space-6) var(--sk-space-4) var(--sk-space-8);
        background: var(--sk-canvas);
      }

      @media (min-width: 1000px) {
        .form {
          align-items: center;
          padding: var(--sk-space-7) var(--sk-space-7);
        }
      }

      .form__inner {
        width: 100%;
        max-width: 34rem;
        margin: 0 auto;
      }

      .form__head {
        margin-bottom: var(--sk-space-5);
      }

      .form__head h1 {
        margin-bottom: var(--sk-space-1);
      }
    `,
  ],
})
export class AuthLayout {
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
}
