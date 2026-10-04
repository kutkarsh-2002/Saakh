import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AvailabilityStatus, DealState, InterestStatus, VerificationStatus } from '../core/models/domain';
import { StatusPill } from './status-pill';

/**
 * The design brief's hard rule: status is never conveyed by colour alone. Every
 * pill has to put an icon and a word on screen next to the colour, because a
 * colour-blind vendor on a sun-washed phone screen is the normal case here, not
 * an edge case.
 */
describe('StatusPill', () => {
  let fixture: ComponentFixture<StatusPill>;

  const render = (inputs: Record<string, unknown>) => {
    fixture = TestBed.createComponent(StatusPill);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => TestBed.configureTestingModule({}));

  it('renders an icon and a word beside the colour for a deal state', () => {
    const host = render({ dealState: DealState.Halted });

    expect(host.querySelector('.pill__label')?.textContent?.trim()).toBe('Halted');
    expect(host.querySelector('.pill__icon')?.textContent?.trim()).toBeTruthy();
    expect(host.querySelector<HTMLElement>('.pill')?.style.getPropertyValue('--pill-color')).toContain(
      'var(--sk-',
    );
  });

  it('keeps the icon out of the accessibility tree, so the word is read once', () => {
    const host = render({ dealState: DealState.Completed });

    expect(host.querySelector('.pill__icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('shows Progress as the one state that moves', () => {
    const host = render({ dealState: DealState.Progress });

    expect(host.querySelector('.pill__icon--spin')).not.toBeNull();
  });

  it('does not animate a settled state', () => {
    const host = render({ dealState: DealState.Completed });

    expect(host.querySelector('.pill__icon--spin')).toBeNull();
  });

  it('labels each vocabulary it is given, not just deal states', () => {
    expect(
      render({ verification: VerificationStatus.NeedsApproval }).textContent,
    ).toContain('Needs approval');

    expect(render({ availability: AvailabilityStatus.Suspended }).textContent).toContain('Suspended');
    expect(render({ interest: InterestStatus.Declined }).textContent).toContain('Declined');
  });

  it('falls back to a readable unknown rather than an empty pill', () => {
    const host = render({});

    expect(host.querySelector('.pill__label')?.textContent?.trim()).toBe('Unknown');
  });

  it('carries the status meaning as a tooltip, and can be asked not to', () => {
    const withTooltip = TestBed.createComponent(StatusPill);
    withTooltip.componentRef.setInput('dealState', DealState.Halted);
    withTooltip.detectChanges();
    expect(withTooltip.componentInstance.descriptor().meaning).toBeTruthy();

    const host = render({ dealState: DealState.Halted, showTooltip: false });
    expect(host.querySelector('.pill')?.getAttribute('aria-describedby')).toBeNull();
  });
});
