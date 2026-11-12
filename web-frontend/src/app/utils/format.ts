const moneyFormats = new Map<string, Intl.NumberFormat>();

export function formatMoney(
  value: number | null | undefined,
  currency = 'USD',
  digits = 0,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '-';
  const key = `${currency}:${digits}`;
  let fmt = moneyFormats.get(key);
  if (!fmt) {
    fmt = new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency,
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    });
    moneyFormats.set(key, fmt);
  }
  return fmt.format(value);
}

export function formatCompact(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '-';
  const abs = Math.abs(value);
  const sign = value < 0 ? '-' : '';
  if (abs >= 1e9) return `${sign}${(abs / 1e9).toFixed(1)}B`;
  if (abs >= 1e6) return `${sign}${(abs / 1e6).toFixed(1)}M`;
  if (abs >= 1e3) return `${sign}${(abs / 1e3).toFixed(0)}K`;
  return `${sign}${abs.toFixed(0)}`;
}

export function formatPct(
  value: number | null | undefined,
  digits = 0,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '-';
  return `${(value * 100).toFixed(digits)}%`;
}

export function formatNumber(
  value: number | null | undefined,
  digits = 2,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '-';
  return value.toLocaleString('en-US', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  });
}

export function statusTone(
  status: string,
): 'good' | 'warn' | 'bad' | 'neutral' {
  switch (status) {
    case 'Pass':
    case 'Ok':
    case 'Effective':
    case 'Approved':
    case 'Released':
    case 'Active':
      return 'good';
    case 'Warning':
    case 'PendingApproval':
    case 'Draft':
    case 'NotAssessable':
      return 'warn';
    case 'Breach':
    case 'Ineffective':
    case 'Rejected':
    case 'Cancelled':
      return 'bad';
    default:
      return 'neutral';
  }
}

export function errorMessage(error: unknown): string {
  if (typeof error === 'object' && error !== null) {
    const e = error as {
      error?: { message?: string };
      message?: string;
      status?: number;
    };
    if (e.error && typeof e.error.message === 'string') return e.error.message;
    if (e.status === 0) return 'The server could not be reached';
    if (typeof e.message === 'string') return e.message;
  }
  return 'Something went wrong';
}

export function valueOf(event: Event): string {
  return (event.target as HTMLInputElement | HTMLSelectElement).value;
}

export function todayPlus(days: number, from = new Date()): string {
  const d = new Date(from.getTime() + days * 86_400_000);
  return d.toISOString().slice(0, 10);
}
