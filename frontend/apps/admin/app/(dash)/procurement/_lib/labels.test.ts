import { describe, expect, it } from 'vitest';
import { purchaseItemStatusLabel, purchaseItemStatusTone } from './labels';

/**
 * 鐵則 5：enum 一律容忍未知值。後端在 `PurchaseItemStatus` 加新成員不算破壞性變更，
 * 畫面要退回顯示原始字串，不能是空字串也不能丟例外。
 */
const PURCHASE_ITEM_STATUSES = ['Pending', 'Purchased', 'PriceChangedPendingConfirmation', 'Unavailable'] as const;

const UNKNOWN = 'SomeStatusTheBackendAddedLater';

describe('purchaseItemStatusLabel', () => {
  it('四個狀態都有中文標籤，而且不是原始英文', () => {
    for (const status of PURCHASE_ITEM_STATUSES) {
      const label = purchaseItemStatusLabel(status);
      expect(label, status).not.toBe('');
      expect(label, status).not.toBe(status);
    }
  });

  it('四個標籤互不重複', () => {
    const labels = PURCHASE_ITEM_STATUSES.map(purchaseItemStatusLabel);
    expect(new Set(labels).size).toBe(PURCHASE_ITEM_STATUSES.length);
  });

  it('未知狀態退回原始字串，不是空字串也不丟例外', () => {
    expect(purchaseItemStatusLabel(UNKNOWN)).toBe(UNKNOWN);
  });
});

describe('purchaseItemStatusTone', () => {
  it('未知狀態退回 neutral，不丟例外', () => {
    expect(purchaseItemStatusTone(UNKNOWN)).toBe('neutral');
  });

  it('缺貨是 danger——現場最需要注意的狀態', () => {
    expect(purchaseItemStatusTone('Unavailable')).toBe('danger');
  });

  it('已買到是 success', () => {
    expect(purchaseItemStatusTone('Purchased')).toBe('success');
  });
});
