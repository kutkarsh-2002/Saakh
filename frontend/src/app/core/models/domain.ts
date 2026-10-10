// =============================================================================
// Domain model, mirroring the API DTOs
// =============================================================================
// Enum numeric values match the C# enums exactly; the API serialises them as
// numbers, so these must stay in step with backend/Saakh.Api/Domain/Enums.cs.
// =============================================================================

export enum ProfileRole {
  Lender = 1,
  Seeker = 2,
}

export enum VerificationStatus {
  NeedsApproval = 1,
  Pending = 2,
  Active = 3,
  Rejected = 4,
}

export enum AvailabilityStatus {
  Active = 1,
  Inactive = 2,
  Suspended = 3,
  Removed = 4,
}

export enum DealCategory {
  Money = 1,
  RawMaterial = 2,
}

export enum InterestStatus {
  Sent = 1,
  Accepted = 2,
  Declined = 3,
}

export enum DealState {
  Open = 1,
  Progress = 2,
  Halted = 3,
  Completed = 4,
}

export enum BusinessSize {
  Individual = 1,
  Small = 2,
  Medium = 3,
  Large = 4,
}

export enum EvidenceDecision {
  AwaitingReview = 1,
  Approved = 2,
  Rejected = 3,
}

export enum AdminActionType {
  Warning = 1,
  Suspend = 2,
  Remove = 3,
  VerificationApproved = 4,
  VerificationRejected = 5,
  SuspensionLifted = 6,
}

export enum SuspensionDuration {
  OneWeek = 1,
  TwoWeeks = 2,
  OneMonth = 3,
  Permanent = 4,
}

export enum ResumeRequestStatus {
  Pending = 1,
  Accepted = 2,
  Declined = 3,
}

/** Where a set of proposed deal terms stands, before any deal exists. */
export enum ProposalStatus {
  Pending = 1,
  Accepted = 2,
  Countered = 3,
  Withdrawn = 4,
}

export interface CategorySubType {
  id: number;
  category: DealCategory;
  key: string;
  displayName: string;
  defaultUnit: string;
}

/**
 * The full settled history rather than one blended average, so a counterparty
 * can see what the number is made of.
 */
export interface TrustSummary {
  totalDeals: number;
  completedDeals: number;
  haltedDeals: number;
  activeDeals: number;
  ratingsReceived: number;
  averageStars: number | null;
  /** Counts of 1-star through 5-star; index 0 is 1 star. */
  starCounts: number[];
  /** Deals this profile was auto-flagged at fault for under the v1 halt rule. */
  haltsAtFault: number;
  /**
   * Deals the platform closed because the agreed settlement date passed. Recorded
   * against both parties, and kept apart from the star average so a machine finding
   * is never mistaken for what a counterparty said.
   */
  dealsClosedOverdue: number;
}

export interface ProfileSummary {
  id: string;
  role: ProfileRole;
  name: string;
  isBusiness: boolean;
  businessSize: BusinessSize;
  gstinVerified: boolean;
  gstinMasked: string | null;
  gstinLegalName: string | null;
  verificationStatus: VerificationStatus;
  availabilityStatus: AvailabilityStatus;
  suspensionEndDate: string | null;
  rejectionReason: string | null;
  country: string;
  state: string;
  district: string;
  category: DealCategory;
  subType: CategorySubType | null;
  capacityMin: number;
  capacityMax: number;
  capacityUnit: string;
  ownTradeDescription: string | null;
  phone: string | null;
  createdAt: string;
  trust: TrustSummary;
}

export interface OpportunityRow {
  profile: ProfileSummary;
  interestStatus: InterestStatus | null;
  interestId: string | null;
  interestWasSentByMe: boolean;
  hasActiveDeal: boolean;
  matchScore: number;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AdminSummary {
  id: string;
  name: string;
}

export interface Session {
  userId: string;
  email: string;
  fullName: string;
  roles: string[];
  profile: ProfileSummary | null;
  admin: AdminSummary | null;
}

export interface AuthResult {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  session: Session;
}

export interface EvidenceDocument {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  documentType: string;
  submittedAt: string;
  decision: EvidenceDecision;
  rejectionReason: string | null;
  reviewedAt: string | null;
}

export interface VerificationState {
  status: VerificationStatus;
  gstinVerified: boolean;
  rejectionReason: string | null;
  featuresUnlocked: boolean;
  evidence: EvidenceDocument[];
  lastSubmittedAt: string | null;
}

/**
 * The GSTIN format, mirrored from `GstinFormat.Pattern` on the server so a typo is
 * caught before it spends a provider credit. One definition for the signup form and
 * the add-a-GSTIN form, because two copies would drift.
 */
export const GSTIN_PATTERN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$/;

/** The result of adding a GSTIN to an account that signed up without one. */
export interface AddGstinResult {
  verified: boolean;
  /** The registry was unreachable; the number is kept and retried in the background. */
  retrying: boolean;
  message: string;
  profile: ProfileSummary;
}

/** Terms one party has put to the other. Agreeing them is what creates the deal. */
export interface DealProposal {
  id: string;
  interestId: string;
  proposedBy: ProfileSummary;
  proposedByMe: boolean;
  category: DealCategory;
  subType: CategorySubType | null;
  capacity: number;
  capacityUnit: string;
  materialDescription: string | null;
  description: string;
  estimatedSettlementTime: string;
  status: ProposalStatus;
  /** True when it is this viewer's turn to agree or amend. */
  awaitingMyResponse: boolean;
  dealId: string | null;
  createdAt: string;
  respondedAt: string | null;
}

export interface ProposalThread {
  live: DealProposal | null;
  history: DealProposal[];
}

export interface Interest {
  id: string;
  counterparty: ProfileSummary;
  sentByMe: boolean;
  status: InterestStatus;
  note: string | null;
  createdAt: string;
  respondedAt: string | null;
  chatUnlocked: boolean;
  dealId: string | null;
  unreadMessages: number;
}

export interface Message {
  id: string;
  dealId: string | null;
  interestId: string | null;
  senderProfileId: string;
  senderName: string;
  body: string;
  sentAt: string;
  mine: boolean;
}

export interface Rating {
  id: string;
  dealId: string;
  raterProfileId: string;
  raterName: string;
  ratedProfileId: string;
  stars: number;
  comment: string | null;
  createdAt: string;
}

export interface DealStateHistory {
  id: string;
  fromState: DealState | null;
  toState: DealState;
  triggeredByProfileId: string | null;
  triggeredByName: string | null;
  note: string | null;
  occurredAt: string;
}

export interface ResumeRequest {
  id: string;
  dealId: string;
  requestedByProfileId: string;
  requestedByMe: boolean;
  status: ResumeRequestStatus;
  createdAt: string;
}

export interface DealRow {
  id: string;
  reference: string;
  category: DealCategory;
  subType: CategorySubType | null;
  description: string;
  capacity: number;
  capacityUnit: string;
  materialDescription: string | null;
  country: string;
  state: string;
  district: string;
  estimatedSettlementTime: string;
  dealState: DealState;
  counterparty: ProfileSummary;
  mySide: ProfileRole;
  iHaltedThisDeal: boolean;
  haltedByProfileId: string | null;
  haltedByName: string | null;
  haltedAt: string | null;
  mySettlementConfirmed: boolean;
  theirSettlementConfirmed: boolean;
  createdAt: string;
  closedAt: string | null;
  ratingGiven: Rating | null;
  ratingReceived: Rating | null;
  canRate: boolean;
  pendingResumeRequest: ResumeRequest | null;
  frozen: boolean;
  /** The platform closed this deal because the agreed settlement date passed. */
  closedOverdue: boolean;
}

export interface DealDetail {
  deal: DealRow;
  timeline: DealStateHistory[];
  messages: Message[];
  ratings: Rating[];
  allowedTransitions: DealState[];
}

export interface OpenedVsClosedPoint {
  period: string;
  cumulativeOpened: number;
  cumulativeClosed: number;
}

export interface SettlementQualityPoint {
  period: string;
  completedClean: number;
  halted: number;
}

export interface DashboardAnalytics {
  openedVsClosed: OpenedVsClosedPoint[];
  settlementQuality: SettlementQualityPoint[];
  trust: TrustSummary;
}

export interface AppNotification {
  id: string;
  kind: string;
  title: string;
  body: string;
  link: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface LocationOption {
  country: string;
  state: string;
  districts: string[];
}

export interface GstinCheckResult {
  isValid: boolean;
  legalName: string | null;
  status: string | null;
  message: string;
  retrying: boolean;
}

export interface OtpRequestResult {
  /** False when a code was already sent moments ago; the existing one still stands. */
  sent: boolean;
  message: string;
  /** Returned only where the demo channel is on, so the flow works with no SMS gateway. */
  devCode: string | null;
  /** Seconds before another code can be asked for. */
  retryAfterSeconds: number;
  /** Seconds the issued code remains valid. */
  expiresInSeconds: number;
}

// ---- Admin ------------------------------------------------------------------

export interface VerificationQueueRow {
  profile: ProfileSummary;
  email: string;
  phone: string | null;
  phoneVerified: boolean;
  evidence: EvidenceDocument[];
  submittedAt: string | null;
  hoursWaiting: number | null;
}

export interface AdminActionLog {
  id: string;
  adminId: string;
  adminName: string;
  targetProfileId: string;
  targetProfileName: string;
  actionType: AdminActionType;
  suspensionDuration: SuspensionDuration | null;
  notes: string | null;
  occurredAt: string;
}

export interface AdminOverview {
  totalProfiles: number;
  activeProfiles: number;
  needsApproval: number;
  pendingReview: number;
  rejected: number;
  suspended: number;
  removed: number;
  openDeals: number;
  haltedDeals: number;
  completedDeals: number;
}

export interface AdminProfileDetail {
  profile: ProfileSummary;
  email: string;
  phoneVerified: boolean;
  evidence: EvidenceDocument[];
  actionLog: AdminActionLog[];
  deals: DealRow[];
}

export interface ApiError {
  message: string;
  code: string | null;
  errors: Record<string, string[]> | null;
}
