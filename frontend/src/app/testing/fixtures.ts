import {
  AuthResult,
  AvailabilityStatus,
  BusinessSize,
  DealCategory,
  DealRow,
  DealState,
  ProfileRole,
  ProfileSummary,
  Session,
  TrustSummary,
  VerificationStatus,
} from '../core/models/domain';

/**
 * Builders for the shapes the API returns, so a spec states only the one field
 * it is about. Every default here is a plausible live record — a spec that
 * passes against these is not passing against an empty object.
 */

export const trust = (overrides: Partial<TrustSummary> = {}): TrustSummary => ({
  totalDeals: 12,
  completedDeals: 9,
  haltedDeals: 1,
  activeDeals: 2,
  ratingsReceived: 9,
  averageStars: 4.3,
  starCounts: [0, 0, 1, 3, 5],
  haltsAtFault: 0,
  ...overrides,
});

export const profile = (overrides: Partial<ProfileSummary> = {}): ProfileSummary => ({
  id: 'aa000000-0000-0000-0000-000000000001',
  role: ProfileRole.Seeker,
  name: 'Mahaveer Textiles',
  isBusiness: true,
  businessSize: BusinessSize.Small,
  gstinVerified: true,
  gstinMasked: '27XXXXXXXXXX1ZV',
  gstinLegalName: 'MAHAVEER TEXTILES PRIVATE LIMITED',
  verificationStatus: VerificationStatus.Active,
  availabilityStatus: AvailabilityStatus.Active,
  suspensionEndDate: null,
  rejectionReason: null,
  country: 'India',
  state: 'Maharashtra',
  district: 'Pune',
  category: DealCategory.RawMaterial,
  subType: { id: 4, category: DealCategory.RawMaterial, key: 'cotton', displayName: 'Cotton', defaultUnit: 'kg' },
  capacityMin: 1000,
  capacityMax: 5000,
  capacityUnit: 'kg',
  ownTradeDescription: null,
  phone: null,
  createdAt: '2026-01-14T06:30:00+05:30',
  trust: trust(),
  ...overrides,
});

export const session = (overrides: Partial<Session> = {}): Session => ({
  userId: 'bb000000-0000-0000-0000-000000000001',
  email: 'vendor@example.com',
  fullName: 'Rohit Shah',
  roles: ['Seeker'],
  profile: profile(),
  admin: null,
  ...overrides,
});

export const adminSession = (): Session =>
  session({
    roles: ['Admin'],
    profile: null,
    admin: { id: 'cc000000-0000-0000-0000-000000000001', name: 'Reviewer One' },
  });

export const authResult = (overrides: Partial<AuthResult> = {}): AuthResult => ({
  accessToken: 'access-token-1',
  refreshToken: 'refresh-token-1',
  accessTokenExpiresAt: '2026-10-04T12:00:00Z',
  session: session(),
  ...overrides,
});

export const deal = (overrides: Partial<DealRow> = {}): DealRow => ({
  id: 'dd000000-0000-0000-0000-000000000001',
  reference: 'SK-2026-0042',
  category: DealCategory.RawMaterial,
  subType: null,
  description: 'Cotton lint, 40 bales, delivered to Pune',
  capacity: 4000,
  capacityUnit: 'kg',
  materialDescription: 'Shankar-6 cotton lint',
  country: 'India',
  state: 'Maharashtra',
  district: 'Pune',
  estimatedSettlementTime: '2026-11-20T00:00:00+05:30',
  dealState: DealState.Progress,
  counterparty: profile({ id: 'aa000000-0000-0000-0000-000000000002', name: 'Suryoday Spinners' }),
  mySide: ProfileRole.Seeker,
  iHaltedThisDeal: false,
  haltedByProfileId: null,
  haltedByName: null,
  haltedAt: null,
  mySettlementConfirmed: false,
  theirSettlementConfirmed: false,
  createdAt: '2026-09-01T09:00:00+05:30',
  closedAt: null,
  ratingGiven: null,
  ratingReceived: null,
  canRate: false,
  pendingResumeRequest: null,
  frozen: false,
  ...overrides,
});
