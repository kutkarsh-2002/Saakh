import { DealCategory } from '../models/domain';

/**
 * Money is rendered in the Indian numbering system (lakh/crore grouping),
 * because that is how these amounts are read aloud and written on an invoice.
 */
const INR = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

const PLAIN = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });

export function formatCapacity(value: number, unit: string, category: DealCategory): string {
  if (category === DealCategory.Money || unit.toUpperCase() === 'INR') {
    return INR.format(value);
  }
  return `${PLAIN.format(value)} ${unit}`;
}

export function formatRange(
  min: number,
  max: number,
  unit: string,
  category: DealCategory,
): string {
  if (category === DealCategory.Money || unit.toUpperCase() === 'INR') {
    return `${INR.format(min)} – ${INR.format(max)}`;
  }
  return `${PLAIN.format(min)} – ${PLAIN.format(max)} ${unit}`;
}

export function formatNumber(value: number): string {
  return PLAIN.format(value);
}

export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Date(value).toLocaleDateString('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Date(value).toLocaleString('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Relative wording, which reads better than a timestamp in a chat or a queue. */
export function formatRelative(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const then = new Date(value).getTime();
  const minutes = Math.round((Date.now() - then) / 60000);

  if (minutes < 1) {
    return 'just now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return `${hours} ${hours === 1 ? 'hour' : 'hours'} ago`;
  }

  const days = Math.round(hours / 24);
  if (days < 31) {
    return `${days} ${days === 1 ? 'day' : 'days'} ago`;
  }

  return formatDate(value);
}

/** Days until a settlement date; negative once it has passed. */
export function daysUntil(value: string): number {
  const target = new Date(value).getTime();
  return Math.ceil((target - Date.now()) / 86400000);
}

export function settlementUrgency(value: string): 'overdue' | 'soon' | 'ok' {
  const days = daysUntil(value);
  if (days < 0) {
    return 'overdue';
  }
  return days <= 7 ? 'soon' : 'ok';
}
