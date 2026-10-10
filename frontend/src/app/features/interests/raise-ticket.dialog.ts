import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import {
  CategorySubType,
  DealCategory,
  DealProposal,
  ProfileSummary,
} from '../../core/models/domain';
import { RaiseTicketPayload } from '../../core/api/saakh.api';
import { controlSignal } from '../../core/util/forms';
import { formatCapacity } from '../../core/util/format';

export interface RaiseTicketDialogData {
  interestId: string;
  counterparty: ProfileSummary;
  taxonomy: CategorySubType[];
  /**
   * Terms being amended. When set, the form opens on what the other party proposed
   * so the changes read as edits rather than a fresh form filled in again.
   */
  existing?: DealProposal | null;
}

/**
 * The ticket: the structured form both sides agree on, which creates the Deal in
 * Open state. It branches by category — Money carries an amount and currency,
 * Raw Material a quantity, unit and material description.
 *
 * The dialog says plainly that Saakh does not move the money or the material,
 * because a vendor raising their first ticket needs to know this is a record,
 * not an escrow.
 */
@Component({
  selector: 'sk-raise-ticket-dialog',
  imports: [MatDialogModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>
      {{ amending ? 'Amend the terms' : 'Propose terms to ' + data.counterparty.name }}
    </h2>

    <div mat-dialog-content class="content">
      <p class="intro">
        @if (amending) {
          Change what you need to. These terms replace what {{ data.counterparty.name }} proposed,
          and it goes back to them to agree.
        } @else {
          This puts terms to {{ data.counterparty.name }}. The deal opens in
          <strong>Open</strong> state once they agree them — not before.
        }
      </p>

      <!-- The date is the one term the platform acts on by itself, so what that
           means is said here rather than discovered a week after it passes. -->
      <p class="intro intro--warn">
        <span class="material-symbols-rounded" aria-hidden="true">schedule</span>
        <span>
          If the deal is still unsettled a week after the settlement date, the platform closes it
          and records that on <strong>both</strong> of your trust records.
        </span>
      </p>

      <form class="form" [formGroup]="form">
        <fieldset class="cats">
          <legend class="sk-eyebrow">What is being provided</legend>
          <div class="cats__options">
            @for (option of [DealCategory.Money, DealCategory.RawMaterial]; track option) {
              <label class="cat" [class.cat--on]="category() === option">
                <input
                  type="radio"
                  name="category"
                  [value]="option"
                  [checked]="category() === option"
                  (change)="pickCategory(option)"
                />
                <span class="material-symbols-rounded" aria-hidden="true">
                  {{ option === DealCategory.Money ? 'payments' : 'inventory_2' }}
                </span>
                <span>{{ option === DealCategory.Money ? 'Money' : 'Raw material' }}</span>
              </label>
            }
          </div>
        </fieldset>

        <div class="sk-field">
          <label for="subType">
            {{ category() === DealCategory.Money ? 'Type of credit' : 'Material type' }}
          </label>
          <select
            id="subType"
            class="sk-select"
            formControlName="categorySubTypeId"
            (change)="onSubTypeChange()"
          >
            @for (option of subTypes(); track option.id) {
              <option [value]="option.id">{{ option.displayName }}</option>
            }
          </select>
        </div>

        <div class="sk-grid-2">
          <div class="sk-field">
            <label for="capacity">
              {{ category() === DealCategory.Money ? 'Amount' : 'Quantity' }}
            </label>
            <input
              id="capacity"
              type="number"
              class="sk-input"
              formControlName="capacity"
              min="1"
              inputmode="numeric"
              [class.is-invalid]="invalid('capacity')"
            />
            @if (invalid('capacity')) {
              <p class="sk-error">
                <span class="material-symbols-rounded" aria-hidden="true">error</span>
                Enter the amount or quantity you agreed.
              </p>
            }
          </div>

          <div class="sk-field">
            <label for="capacityUnit">
              {{ category() === DealCategory.Money ? 'Currency' : 'Unit' }}
            </label>
            <input
              id="capacityUnit"
              type="text"
              class="sk-input"
              formControlName="capacityUnit"
              list="capacityUnitOptions"
              autocomplete="off"
              maxlength="32"
            />
            <!-- A list to pick from, and still a plain text field: this market
                 counts in units no fixed list will ever cover. -->
            <datalist id="capacityUnitOptions">
              @for (option of unitOptions(); track option) {
                <option [value]="option"></option>
              }
            </datalist>
            <p class="sk-hint">
              {{
                category() === DealCategory.Money
                  ? 'Pick a currency or type another one.'
                  : 'Pick one, or type whatever you both count in.'
              }}
            </p>
          </div>
        </div>

        @if (capacityPreview()) {
          <p class="preview">
            <span class="material-symbols-rounded" aria-hidden="true">receipt</span>
            This deal will read as <strong>{{ capacityPreview() }}</strong>
          </p>
        }

        <!-- Raw Material carries a material description; Money does not. -->
        @if (category() === DealCategory.RawMaterial) {
          <div class="sk-field">
            <label for="material">What exactly is the material?</label>
            <input
              id="material"
              type="text"
              class="sk-input"
              formControlName="materialDescription"
              maxlength="500"
              placeholder="e.g. Nagpur oranges, grade A, in 20 kg crates"
              [class.is-invalid]="invalid('materialDescription')"
            />
            @if (invalid('materialDescription')) {
              <p class="sk-error">
                <span class="material-symbols-rounded" aria-hidden="true">error</span>
                Describe the material, so neither side can be in doubt later.
              </p>
            }
          </div>
        }

        <div class="sk-field">
          <label for="settlement">Estimated settlement date</label>
          <input
            id="settlement"
            type="date"
            class="sk-input"
            formControlName="settlementDate"
            [attr.min]="today"
            [class.is-invalid]="invalid('settlementDate')"
          />
          <p class="sk-hint">
            When you both expect the obligations to be cleared. It is an estimate, not a deadline
            the platform enforces.
          </p>
          @if (invalid('settlementDate')) {
            <p class="sk-error">
              <span class="material-symbols-rounded" aria-hidden="true">error</span>
              Pick a settlement date in the future.
            </p>
          }
        </div>

        <div class="sk-field">
          <label for="description">The arrangement, in your own words</label>
          <textarea
            id="description"
            class="sk-textarea"
            formControlName="description"
            rows="3"
            maxlength="2000"
            placeholder="e.g. Stock on 30-day credit, delivered weekly from the 5th, paid in one instalment on settlement."
            [class.is-invalid]="invalid('description')"
          ></textarea>
          <p class="sk-hint">
            Both of you can read this later, and so can an administrator if the deal is ever
            disputed. Concrete beats polite.
          </p>
          @if (invalid('description')) {
            <p class="sk-error">
              <span class="material-symbols-rounded" aria-hidden="true">error</span>
              Write a short description of what you agreed.
            </p>
          }
        </div>
      </form>

      <p class="disclaimer">
        <span class="material-symbols-rounded" aria-hidden="true">info</span>
        <span>
          Saakh does not hold, move or guarantee the money or the material. The transfer happens
          between the two of you, off the platform; this records what you agreed and tracks how it
          ends.
        </span>
      </p>
    </div>

    <div mat-dialog-actions class="actions">
      <button type="button" class="sk-btn sk-btn--secondary" (click)="cancel()">Cancel</button>
      <button type="button" class="sk-btn sk-btn--primary" (click)="submit()">
        <span class="material-symbols-rounded" aria-hidden="true">post_add</span>
        {{ amending ? 'Send amended terms' : 'Send terms' }}
      </button>
    </div>
  `,
  styleUrl: './raise-ticket.dialog.scss',
})
export class RaiseTicketDialog {
  readonly data = inject<RaiseTicketDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<RaiseTicketDialog, RaiseTicketPayload | null>>(MatDialogRef);
  private readonly fb = inject(FormBuilder);

  readonly DealCategory = DealCategory;
  readonly today = new Date().toISOString().slice(0, 10);

  readonly category = signal<DealCategory>(this.data.counterparty.category);

  readonly form = this.fb.nonNullable.group({
    categorySubTypeId: [null as number | null, Validators.required],
    capacity: [0, [Validators.required, Validators.min(1)]],
    capacityUnit: ['INR', [Validators.required]],
    materialDescription: [''],
    description: ['', [Validators.required, Validators.maxLength(2000)]],
    // Defaults to 30 days out, which is the most common term in this market.
    settlementDate: [defaultSettlementDate(), Validators.required],
  });

  readonly subTypes = computed(() =>
    this.data.taxonomy.filter((option) => option.category === this.category()),
  );

  // Read as signals, not as `.value` inside a computed — see `controlSignal`.
  // The live preview below the figure is the whole point of this dialog's form,
  // and it is exactly what silently stops updating when this is got wrong.
  private readonly selectedSubTypeId = controlSignal(this.form.controls.categorySubTypeId);
  private readonly capacityValue = controlSignal(this.form.controls.capacity);
  private readonly unitValue = controlSignal(this.form.controls.capacityUnit);

  /**
   * What this trade is counted in. The list is a shortcut, not a constraint —
   * the field stays free text, because vendors here settle in units no fixed
   * list covers, and forcing a choice would make them record the wrong figure.
   * The sub-type's own default leads, since it is the likeliest answer.
   */
  readonly unitOptions = computed(() => {
    const common =
      this.category() === DealCategory.Money
        ? MONEY_UNITS
        : MATERIAL_UNITS;

    const preferred = this.subTypes().find(
      (option) => option.id === Number(this.selectedSubTypeId()),
    )?.defaultUnit;

    return [...new Set([preferred, ...common].filter((unit): unit is string => !!unit))];
  });

  readonly capacityPreview = computed(() => {
    const value = Number(this.capacityValue());
    if (!value) {
      return null;
    }
    return formatCapacity(value, this.unitValue(), this.category());
  });

  /** True when answering terms that were put to us, rather than opening a negotiation. */
  readonly amending = !!this.data.existing;

  constructor() {
    const existing = this.data.existing;

    if (existing) {
      // Amending starts from their terms so the difference is visible; the date is
      // the one field most counters change.
      this.category.set(existing.category);
      this.applyCategoryDefaults(existing.subType?.id ?? null);

      this.form.patchValue({
        categorySubTypeId: existing.subType?.id ?? null,
        capacity: existing.capacity,
        capacityUnit: existing.capacityUnit,
        materialDescription: existing.materialDescription ?? '',
        description: existing.description,
        settlementDate: existing.estimatedSettlementTime.slice(0, 10),
      });

      return;
    }

    // Seed the form from the counterparty's own declared sub-type: it is the
    // most likely answer, and it keeps the shared taxonomy consistent.
    this.applyCategoryDefaults(this.data.counterparty.subType?.id ?? null);
  }

  private applyCategoryDefaults(preferredSubTypeId: number | null): void {
    const options = this.subTypes();
    const chosen =
      options.find((option) => option.id === preferredSubTypeId) ?? options[0] ?? null;

    this.form.controls.categorySubTypeId.setValue(chosen?.id ?? null);
    this.form.controls.capacityUnit.setValue(
      chosen?.defaultUnit ?? (this.category() === DealCategory.Money ? 'INR' : 'units'),
    );

    const material = this.form.controls.materialDescription;
    if (this.category() === DealCategory.RawMaterial) {
      material.addValidators(Validators.required);
    } else {
      material.removeValidators(Validators.required);
      material.setValue('');
    }
    material.updateValueAndValidity();
  }

  /** Keeps the unit in step with the material type the user picks. */
  onSubTypeChange(): void {
    const chosen = this.subTypes().find(
      (option) => option.id === Number(this.form.controls.categorySubTypeId.value),
    );

    if (chosen?.defaultUnit) {
      this.form.controls.capacityUnit.setValue(chosen.defaultUnit);
    }
  }

  pickCategory(category: DealCategory): void {
    this.category.set(category);
    this.applyCategoryDefaults(null);
  }

  invalid(control: string): boolean {
    const field = this.form.get(control);
    return !!field && field.invalid && (field.dirty || field.touched);
  }

  cancel(): void {
    this.dialogRef.close(null);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();

    this.dialogRef.close({
      interestId: this.data.interestId,
      category: this.category(),
      categorySubTypeId: value.categorySubTypeId,
      capacity: Number(value.capacity),
      capacityUnit: value.capacityUnit.trim(),
      materialDescription:
        this.category() === DealCategory.RawMaterial
          ? value.materialDescription.trim()
          : null,
      description: value.description.trim(),
      // Settlement is a date in the user's own timezone; send it as an instant.
      estimatedSettlementTime: new Date(`${value.settlementDate}T12:00:00`).toISOString(),
    });
  }
}

function defaultSettlementDate(): string {
  const date = new Date();
  date.setDate(date.getDate() + 30);
  return date.toISOString().slice(0, 10);
}

/** Currencies an Indian vendor realistically settles a credit line in. */
const MONEY_UNITS = ['INR', 'USD', 'EUR', 'AED', 'GBP'];

/**
 * How raw material is actually counted in this market — weight, volume, and the
 * packing units a consignment is agreed in. Ordered by how often they come up,
 * not alphabetically, so the common answer is the first one in the list.
 */
const MATERIAL_UNITS = [
  'kg',
  'quintal',
  'tonne',
  'litre',
  'crate',
  'bag',
  'sack',
  'box',
  'carton',
  'bundle',
  'bale',
  'piece',
  'dozen',
  'drum',
  'metre',
  'sq ft',
  'units',
];
