import {
  AVAILABILITY_STATUS,
  CATEGORY_META,
  DEAL_STATE_STATUS,
  INTEREST_STATUS,
  ROLE_META,
  StatusDescriptor,
  VERIFICATION_STATUS,
} from './status-vocabulary';
import {
  AvailabilityStatus,
  DealCategory,
  DealState,
  InterestStatus,
  ProfileRole,
  VerificationStatus,
} from './domain';

/**
 * The status vocabulary is the single guarantee behind the design brief's
 * hardest rule: no status is ever conveyed by colour alone. Every pill renders a
 * token-driven colour, an icon and a label together — so a missing entry is not
 * a cosmetic bug, it is a status that becomes unreadable to a colourblind user
 * and renders `undefined` in the DOM.
 */
describe('status vocabulary', () => {
  const allMaps: [string, Record<number, StatusDescriptor>][] = [
    ['deal state', DEAL_STATE_STATUS],
    ['verification', VERIFICATION_STATUS],
    ['availability', AVAILABILITY_STATUS],
    ['interest', INTEREST_STATUS],
  ];

  it('describes every deal state the API can return', () => {
    for (const state of [
      DealState.Open,
      DealState.Progress,
      DealState.Halted,
      DealState.Completed,
    ]) {
      expect(DEAL_STATE_STATUS[state]).toBeDefined();
    }
  });

  it('describes every verification state in the spec state machine', () => {
    for (const status of [
      VerificationStatus.NeedsApproval,
      VerificationStatus.Pending,
      VerificationStatus.Active,
      VerificationStatus.Rejected,
    ]) {
      expect(VERIFICATION_STATUS[status]).toBeDefined();
    }
  });

  it('describes every availability state including the admin-imposed ones', () => {
    for (const status of [
      AvailabilityStatus.Active,
      AvailabilityStatus.Inactive,
      AvailabilityStatus.Suspended,
      AvailabilityStatus.Removed,
    ]) {
      expect(AVAILABILITY_STATUS[status]).toBeDefined();
    }
  });

  it('describes every interest state', () => {
    for (const status of [
      InterestStatus.Sent,
      InterestStatus.Accepted,
      InterestStatus.Declined,
    ]) {
      expect(INTEREST_STATUS[status]).toBeDefined();
    }
  });

  for (const [name, map] of allMaps) {
    it(`gives every ${name} a colour token, an icon and a label`, () => {
      for (const descriptor of Object.values(map)) {
        expect(descriptor.token).toBeTruthy();
        expect(descriptor.icon).toBeTruthy();
        expect(descriptor.label).toBeTruthy();
        expect(descriptor.meaning).toBeTruthy();
      }
    });
  }

  it('gives each status within a group its own icon shape', () => {
    // Two statuses sharing a glyph would be distinguishable only by colour,
    // which is exactly what the brief forbids.
    for (const [name, map] of allMaps) {
      const icons = Object.values(map).map((d) => d.icon);
      expect(new Set(icons).size)
        .withContext(`${name} icons must be unique: ${icons.join(', ')}`)
        .toBe(icons.length);
    }
  });

  it('gives each status within a group its own label', () => {
    for (const [name, map] of allMaps) {
      const labels = Object.values(map).map((d) => d.label);
      expect(new Set(labels).size)
        .withContext(`${name} labels must be unique: ${labels.join(', ')}`)
        .toBe(labels.length);
    }
  });

  it('covers both deal categories and both roles', () => {
    expect(CATEGORY_META[DealCategory.Money]).toBeDefined();
    expect(CATEGORY_META[DealCategory.RawMaterial]).toBeDefined();

    expect(ROLE_META[ProfileRole.Lender].discovers).toBe('Seekers');
    expect(ROLE_META[ProfileRole.Seeker].discovers).toBe('Lenders');
  });
});
