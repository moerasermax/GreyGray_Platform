import { describe, expect, it } from 'vitest';
import { recipientAddressOf } from './recipientAddress';

describe('recipientAddressOf：後台「收件地址」那一格（ADR-039）', () => {
  it('宅配訂單有地址 → 原樣回傳', () => {
    expect(recipientAddressOf({ recipientAddress: '110 台北市信義區松仁路 100 號 5 樓' })).toBe(
      '110 台北市信義區松仁路 100 號 5 樓',
    );
  });

  it('超商取貨訂單（null）→ 回 null，那一格不顯示', () => {
    expect(recipientAddressOf({ recipientAddress: null })).toBeNull();
  });

  it('ADR-039 之前的舊訂單（省略）→ 回 null', () => {
    expect(recipientAddressOf({})).toBeNull();
  });

  it('空字串當作沒有', () => {
    expect(recipientAddressOf({ recipientAddress: '   ' })).toBeNull();
  });
});
