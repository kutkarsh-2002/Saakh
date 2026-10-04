import { DealCategory } from '../models/domain';
import { daysUntil, formatCapacity, formatRange, settlementUrgency } from './format';

/**
 * Figures are the product's most-read content: an amount misread by a factor of
 * ten is a real-world consequence, not a display bug. Money uses the Indian
 * numbering system because that is how these amounts are said aloud and written
 * on an invoice.
 */
describe('formatting figures', () => {
  it('renders money in the Indian numbering system with lakh grouping', () => {
    const formatted = formatCapacity(242000, 'INR', DealCategory.Money);

    // 2,42,000 — not 242,000. The grouping is the point.
    expect(formatted).toContain('2,42,000');
  });

  it('renders a material quantity with its unit rather than a currency symbol', () => {
    const formatted = formatCapacity(1500, 'kg', DealCategory.RawMaterial);

    expect(formatted).toContain('1,500');
    expect(formatted).toContain('kg');
    expect(formatted).not.toContain('₹');
  });

  it('treats an INR unit as money even when the category says otherwise', () => {
    // Guards against a ticket raised with the wrong category showing a bare
    // number where an amount belongs.
    expect(formatCapacity(50000, 'INR', DealCategory.RawMaterial)).toContain('₹');
  });

  it('renders a capacity range with a single unit, not one per end', () => {
    const range = formatRange(1000, 5000, 'kg', DealCategory.RawMaterial);

    expect(range).toContain('1,000');
    expect(range).toContain('5,000');
    expect(range.match(/kg/g)?.length).toBe(1);
  });
});

describe('settlement urgency', () => {
  const inDays = (days: number) =>
    new Date(Date.now() + days * 86400000).toISOString();

  it('counts a future settlement date as days remaining', () => {
    expect(daysUntil(inDays(10))).toBeGreaterThan(8);
    expect(daysUntil(inDays(10))).toBeLessThanOrEqual(11);
  });

  it('counts a past settlement date as negative', () => {
    expect(daysUntil(inDays(-5))).toBeLessThan(0);
  });

  it('flags anything past its estimate as overdue', () => {
    expect(settlementUrgency(inDays(-1))).toBe('overdue');
  });

  it('flags the next seven days as due soon', () => {
    expect(settlementUrgency(inDays(3))).toBe('soon');
    expect(settlementUrgency(inDays(7))).toBe('soon');
  });

  it('leaves anything further out unflagged, so the warning keeps its meaning', () => {
    expect(settlementUrgency(inDays(30))).toBe('ok');
  });
});
