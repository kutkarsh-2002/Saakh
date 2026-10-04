import { ComponentFixture, TestBed } from '@angular/core/testing';
import { VerificationStatus } from '../core/models/domain';
import { VerificationBanner } from './verification-banner';

/**
 * While an account is locked, this banner is the only thing standing between a
 * vendor and a dead-end screen. Each state has to say what happened, what it
 * blocks, and the single next action — and in the Active state it has to
 * disappear rather than linger as decoration.
 */
describe('VerificationBanner', () => {
  let fixture: ComponentFixture<VerificationBanner>;

  const render = (status: VerificationStatus, rejectionReason: string | null = null) => {
    fixture = TestBed.createComponent(VerificationBanner);
    fixture.componentRef.setInput('status', status);
    fixture.componentRef.setInput('rejectionReason', rejectionReason);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => TestBed.configureTestingModule({}));

  it('renders nothing at all once the account is Active', () => {
    expect(render(VerificationStatus.Active).querySelector('.banner')).toBeNull();
  });

  it('tells a Needs Approval account what to upload', () => {
    const host = render(VerificationStatus.NeedsApproval);

    expect(host.querySelector('.banner__title')?.textContent).toContain('Submit proof documents');
    expect(host.querySelector('.banner__action')?.textContent).toContain('Upload documents');
  });

  it('offers no action while a review is already under way', () => {
    const host = render(VerificationStatus.Pending);

    // Offering a resubmit button here would invite a vendor to pile documents
    // onto a decision that is already being made.
    expect(host.querySelector('.banner__action')).toBeNull();
    expect(host.querySelector('.banner__text')?.textContent).toContain('Nothing more is needed');
  });

  it("shows the reviewer's reason on a rejection, and invites a resubmission", () => {
    const host = render(VerificationStatus.Rejected, 'The licence photo was cut off at the edges.');

    expect(host.querySelector('.banner__reason')?.textContent).toContain('cut off at the edges');
    expect(host.querySelector('.banner__action')?.textContent).toContain('Resubmit documents');
  });

  it('does not render an empty reason block when none was given', () => {
    expect(render(VerificationStatus.Rejected, null).querySelector('.banner__reason')).toBeNull();
  });

  it('names every tab that stays locked, on each blocking state', () => {
    for (const status of [
      VerificationStatus.NeedsApproval,
      VerificationStatus.Pending,
      VerificationStatus.Rejected,
    ]) {
      const locked = render(status).querySelector('.banner__locked')?.textContent ?? '';

      for (const tab of ['Opportunity', 'Interests', 'Deals', 'History', 'Analytics']) {
        expect(locked).withContext(`${VerificationStatus[status]} / ${tab}`).toContain(tab);
      }
    }
  });

  it('announces itself to assistive tech without stealing focus', () => {
    expect(render(VerificationStatus.Pending).querySelector('.banner')?.getAttribute('role')).toBe(
      'status',
    );
  });

  it('emits the upload action rather than navigating itself', () => {
    const host = render(VerificationStatus.NeedsApproval);
    const emitted = jasmine.createSpy('action');
    fixture.componentInstance.action.subscribe(emitted);

    host.querySelector<HTMLButtonElement>('.banner__action')?.click();

    expect(emitted).toHaveBeenCalled();
  });
});
