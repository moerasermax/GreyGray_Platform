import { describe, expect, it } from 'vitest';
import { moneyFromMajorInput, moneyToMajorInput } from '../money';

describe('money input conversion', () => {
  it('converts decimal major units without floating-point drift', () => {
    expect(moneyFromMajorInput('10.29', 'TWD')).toEqual({ amountMinor: 1029, currency: 'TWD' });
    expect(moneyFromMajorInput('1,280.5', 'TWD')).toEqual({ amountMinor: 128050, currency: 'TWD' });
  });

  it('honours zero-decimal currencies and rejects excess precision', () => {
    expect(moneyFromMajorInput('1000', 'JPY')).toEqual({ amountMinor: 1000, currency: 'JPY' });
    expect(moneyFromMajorInput('1000.1', 'JPY')).toBeNull();
    expect(moneyFromMajorInput('12.345', 'TWD')).toBeNull();
  });

  it('formats contract money for a number input', () => {
    expect(moneyToMajorInput({ amountMinor: 18050, currency: 'TWD' })).toBe('180.5');
    expect(moneyToMajorInput({ amountMinor: 1000, currency: 'JPY' })).toBe('1000');
  });
});
