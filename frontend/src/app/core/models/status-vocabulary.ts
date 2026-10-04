import {
  AvailabilityStatus,
  BusinessSize,
  DealCategory,
  DealState,
  InterestStatus,
  ProfileRole,
  VerificationStatus,
} from './domain';

/**
 * One status descriptor: the token that colours it, the icon *shape* that
 * identifies it, and the words that name it.
 *
 * No status in this product is ever told apart by hue alone. Every pill, badge
 * and banner renders all three parts of this descriptor together, which is what
 * makes the statuses readable to a colourblind user and in direct sunlight.
 */
export interface StatusDescriptor {
  /** Token suffix, e.g. 'open' resolves to --sk-open / --sk-open-soft. */
  token: string;
  /** Material Symbols glyph, chosen for a distinct silhouette per status. */
  icon: string;
  label: string;
  /** One line explaining what the state means, used in tooltips and banners. */
  meaning: string;
}

export const DEAL_STATE_STATUS: Record<DealState, StatusDescriptor> = {
  [DealState.Open]: {
    token: 'open',
    // Hollow circle: nothing has moved yet.
    icon: 'radio_button_unchecked',
    label: 'Open',
    meaning: 'Ticket raised and terms agreed. Transfer has not started.',
  },
  [DealState.Progress]: {
    token: 'progress',
    // Rotating arrows: money or material is moving.
    icon: 'sync',
    label: 'In progress',
    meaning: 'Money or material has started moving.',
  },
  [DealState.Halted]: {
    token: 'halted',
    // Flag: the spec's own symbol for a halted deal.
    icon: 'flag',
    label: 'Halted',
    meaning: 'Stalled until both parties agree to resume.',
  },
  [DealState.Completed]: {
    token: 'completed',
    // Filled check: terminal and settled.
    icon: 'task_alt',
    label: 'Completed',
    meaning: 'Both parties confirmed the obligations are cleared.',
  },
};

export const VERIFICATION_STATUS: Record<VerificationStatus, StatusDescriptor> = {
  [VerificationStatus.NeedsApproval]: {
    token: 'needs-approval',
    // Triangle: something is required of the user.
    icon: 'warning',
    label: 'Needs approval',
    meaning: 'Submit proof documents to unlock the platform.',
  },
  [VerificationStatus.Pending]: {
    token: 'pending',
    // Clock: waiting on someone else.
    icon: 'hourglass_top',
    label: 'Under review',
    meaning: 'An administrator is reviewing your documents.',
  },
  [VerificationStatus.Active]: {
    token: 'completed',
    // Shield: identity confirmed.
    icon: 'verified_user',
    label: 'Verified',
    meaning: 'Identity confirmed. Full access to the platform.',
  },
  [VerificationStatus.Rejected]: {
    token: 'rejected',
    // Crossed circle: refused, with a reason.
    icon: 'cancel',
    label: 'Not approved',
    meaning: 'Your documents were not accepted. Resubmit to try again.',
  },
};

export const AVAILABILITY_STATUS: Record<AvailabilityStatus, StatusDescriptor> = {
  [AvailabilityStatus.Active]: {
    token: 'completed',
    icon: 'visibility',
    label: 'Active',
    meaning: 'Visible in search and able to receive interest.',
  },
  [AvailabilityStatus.Inactive]: {
    token: 'inactive',
    // Pause: self-chosen and reversible at any time.
    icon: 'pause_circle',
    label: 'Inactive',
    meaning: 'Hidden from search by choice. In-flight deals continue.',
  },
  [AvailabilityStatus.Suspended]: {
    token: 'suspended',
    // Block: imposed from outside, time-bound or permanent.
    icon: 'block',
    label: 'Suspended',
    meaning: 'Hidden by an administrator. In-flight deals are frozen.',
  },
  [AvailabilityStatus.Removed]: {
    token: 'removed',
    icon: 'person_off',
    label: 'Removed',
    meaning: 'Can no longer act on the platform. History retained for audit.',
  },
};

export const INTEREST_STATUS: Record<InterestStatus, StatusDescriptor> = {
  [InterestStatus.Sent]: {
    token: 'open',
    icon: 'outgoing_mail',
    label: 'Awaiting reply',
    meaning: 'Interest sent. Chat opens once it is accepted.',
  },
  [InterestStatus.Accepted]: {
    token: 'completed',
    icon: 'forum',
    label: 'Accepted',
    meaning: 'Chat is open. Negotiate terms, then raise a ticket.',
  },
  [InterestStatus.Declined]: {
    token: 'inactive',
    icon: 'do_not_disturb_on',
    label: 'Declined',
    meaning: 'No deal was created and nothing is recorded against either profile.',
  },
};

export const CATEGORY_META: Record<DealCategory, { icon: string; label: string }> = {
  [DealCategory.Money]: { icon: 'payments', label: 'Money' },
  [DealCategory.RawMaterial]: { icon: 'inventory_2', label: 'Raw material' },
};

export const ROLE_META: Record<ProfileRole, { icon: string; label: string; discovers: string }> = {
  [ProfileRole.Lender]: {
    icon: 'account_balance',
    label: 'Lender',
    // A Lender discovers Seekers, and vice versa: the dashboards are mirrored.
    discovers: 'Seekers',
  },
  [ProfileRole.Seeker]: {
    icon: 'storefront',
    label: 'Seeker',
    discovers: 'Lenders',
  },
};

export const BUSINESS_SIZE_LABEL: Record<BusinessSize, string> = {
  [BusinessSize.Individual]: 'Individual',
  [BusinessSize.Small]: 'Small business',
  [BusinessSize.Medium]: 'Medium business',
  [BusinessSize.Large]: 'Large business',
};
