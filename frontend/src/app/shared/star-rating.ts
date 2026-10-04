import { ChangeDetectionStrategy, Component, computed, input, model, output } from '@angular/core';

/**
 * The 5-star display, and the same component in input mode for submitting a
 * rating. In input mode it renders real radio inputs, so it is keyboard and
 * screen-reader operable rather than a row of clickable spans.
 */
@Component({
  selector: 'sk-star-rating',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (editable()) {
      <fieldset class="input" [attr.aria-describedby]="describedBy()">
        <legend class="sk-sr-only">{{ legend() }}</legend>
        @for (star of stars; track star) {
          <label class="input__star" [class.input__star--on]="star <= value()">
            <input
              type="radio"
              name="{{ name() }}"
              [value]="star"
              [checked]="star === value()"
              (change)="pick(star)"
            />
            <span class="material-symbols-rounded" aria-hidden="true">star</span>
            <span class="sk-sr-only">{{ star }} {{ star === 1 ? 'star' : 'stars' }}</span>
          </label>
        }
      </fieldset>
    } @else {
      <span class="display" [class.display--lg]="size() === 'lg'">
        <span class="display__stars" aria-hidden="true">
          @for (star of stars; track star) {
            <span
              class="material-symbols-rounded"
              [class.on]="star <= rounded()"
              [class.half]="star === rounded() + 1 && hasHalf()"
              >star</span
            >
          }
        </span>

        @if (value() > 0) {
          <span class="display__value sk-figure">{{ value().toFixed(1) }}</span>
        } @else {
          <span class="display__none">No ratings yet</span>
        }

        @if (count() !== null && value() > 0) {
          <span class="display__count">({{ count() }})</span>
        }

        <span class="sk-sr-only">{{ screenReaderText() }}</span>
      </span>
    }
  `,
  styles: [
    `
      :host {
        display: inline-flex;
      }

      .display {
        display: inline-flex;
        align-items: center;
        gap: 6px;
      }

      .display__stars {
        display: inline-flex;
        /* A hair of negative tracking makes five stars read as one object. */
        letter-spacing: -1px;
      }

      .display__stars .material-symbols-rounded {
        font-size: 1.0625rem;
        line-height: 1;
        color: var(--sk-line-strong);
        font-variation-settings: 'FILL' 1;
      }

      .display--lg .display__stars .material-symbols-rounded {
        font-size: 1.5rem;
      }

      .display__stars .on {
        color: var(--sk-star);
      }

      /* A part-filled star is drawn as a gradient clip rather than a second
         glyph, so the row never shifts width between whole and half values. */
      .display__stars .half {
        color: var(--sk-star);
        background: linear-gradient(90deg, var(--sk-star) 50%, var(--sk-line-strong) 50%);
        -webkit-background-clip: text;
        background-clip: text;
        -webkit-text-fill-color: transparent;
      }

      .display__value {
        font-size: 0.875rem;
        color: var(--sk-ink);
      }

      .display--lg .display__value {
        font-size: 1.125rem;
      }

      .display__count {
        font-size: 0.8125rem;
        color: var(--sk-ink-muted);
      }

      .display__none {
        font-size: 0.8125rem;
        color: var(--sk-ink-subtle);
        font-style: italic;
      }

      .input {
        display: flex;
        gap: 2px;
        border: 0;
        margin: 0;
        padding: 0;
      }

      .input__star {
        position: relative;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        /* Full 44px targets: this is tapped one-handed, outdoors. */
        width: var(--sk-touch);
        height: var(--sk-touch);
        border-radius: var(--sk-radius-sm);
        cursor: pointer;
      }

      .input__star input {
        position: absolute;
        inset: 0;
        opacity: 0;
        cursor: pointer;
      }

      .input__star .material-symbols-rounded {
        font-size: 2rem;
        color: var(--sk-line-strong);
        font-variation-settings: 'FILL' 1;
        transition: color 100ms ease, transform 100ms ease;
      }

      .input__star--on .material-symbols-rounded {
        color: var(--sk-star);
      }

      .input__star:hover .material-symbols-rounded {
        transform: scale(1.08);
      }

      .input__star:has(input:focus-visible) {
        outline: 3px solid var(--sk-accent);
        outline-offset: 2px;
      }
    `,
  ],
})
export class StarRating {
  readonly stars = [1, 2, 3, 4, 5];

  /** Average or chosen value. Fractional values render a part-filled star. */
  readonly value = model(0);

  /** Number of ratings behind the average, shown in parentheses. */
  readonly count = input<number | null>(null);

  readonly editable = input(false);
  readonly size = input<'md' | 'lg'>('md');
  readonly name = input('rating');
  readonly legend = input('Rate this counterparty from 1 to 5 stars');
  readonly describedBy = input<string | null>(null);

  readonly picked = output<number>();

  readonly rounded = computed(() => Math.floor(this.value()));
  readonly hasHalf = computed(() => this.value() % 1 >= 0.25);

  readonly screenReaderText = computed(() => {
    const value = this.value();
    if (value <= 0) {
      return 'No ratings yet';
    }
    const count = this.count();
    const base = `${value.toFixed(1)} out of 5 stars`;
    return count === null ? base : `${base} from ${count} ${count === 1 ? 'rating' : 'ratings'}`;
  });

  pick(star: number): void {
    this.value.set(star);
    this.picked.emit(star);
  }
}
