import { describe, expect, it } from 'vitest';
import { recipientDisplayOf } from '../recipientDisplay';

describe('recipientDisplayOf：快照優先，快照為 null 才退回地址簿（ADR-039）', () => {
  it('新訂單：有快照就用快照，不是 fallback', () => {
    const result = recipientDisplayOf({
      recipientName: '王小美',
      recipientPhone: '0912345678',
      shippingAddress: { recipientName: '舊地址簿姓名', phoneNumber: '0900000000' },
    });
    expect(result).toEqual({ recipientName: '王小美', recipientPhone: '0912345678', isFallback: false });
  });

  it('快照只有姓名、手機是 null：仍算有快照，不 fallback', () => {
    const result = recipientDisplayOf({ recipientName: '王小美', recipientPhone: null, shippingAddress: null });
    expect(result).toEqual({ recipientName: '王小美', recipientPhone: '', isFallback: false });
  });

  it('舊訂單：快照兩個都是 null、有地址簿 → 退回地址簿，標記 isFallback', () => {
    const result = recipientDisplayOf({
      recipientName: null,
      recipientPhone: null,
      shippingAddress: { recipientName: '王小美', phoneNumber: '0912345678' },
    });
    expect(result).toEqual({ recipientName: '王小美', recipientPhone: '0912345678', isFallback: true });
  });

  it('舊訂單：快照與地址簿都沒有（例如舊的超商取貨訂單）→ null，畫面什麼都不顯示', () => {
    expect(recipientDisplayOf({ recipientName: null, recipientPhone: null, shippingAddress: null })).toBeNull();
  });

  it('欄位整個缺席（undefined）當作沒有快照處理', () => {
    const result = recipientDisplayOf({ shippingAddress: { recipientName: '王小美', phoneNumber: '0912345678' } });
    expect(result).toEqual({ recipientName: '王小美', recipientPhone: '0912345678', isFallback: true });
  });
});
