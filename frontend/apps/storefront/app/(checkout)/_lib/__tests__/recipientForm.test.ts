/** 收件人姓名手機（ADR-039，超商取貨專用）的驗證與 payload 組裝，純函式測試。 */
import { describe, expect, it } from 'vitest';
import {
  checkoutRecipientPayload,
  isRecipientNameFilled,
  isRecipientPhoneValid,
} from '../recipientForm';

describe('isRecipientNameFilled', () => {
  it('空字串與只有空白都算沒填', () => {
    expect(isRecipientNameFilled('')).toBe(false);
    expect(isRecipientNameFilled('   ')).toBe(false);
  });

  it('有內容就算填了', () => {
    expect(isRecipientNameFilled('王小美')).toBe(true);
  });
});

describe('isRecipientPhoneValid：09 開頭共十碼', () => {
  it.each(['0912345678', '0987654321'])('%s → true', (phone) => {
    expect(isRecipientPhoneValid(phone)).toBe(true);
  });

  it.each([
    ['太短', '0912'],
    ['沒有 09 開頭', '912345678'],
    ['太長', '09123456789'],
    ['空字串', ''],
    ['帶符號', '0912-345678'],
  ])('%s（%s）→ false', (_label, phone) => {
    expect(isRecipientPhoneValid(phone)).toBe(false);
  });
});

describe('checkoutRecipientPayload：只有超商取貨才送', () => {
  it('超商取貨：原樣帶出姓名手機', () => {
    expect(checkoutRecipientPayload('ConvenienceStore', '王小美', '0912345678')).toEqual({
      recipientName: '王小美',
      recipientPhone: '0912345678',
    });
  });

  it('宅配：不送，即使畫面上有殘留值也不送（後端從地址簿抄）', () => {
    expect(checkoutRecipientPayload('HomeDelivery', '王小美', '0912345678')).toEqual({
      recipientName: null,
      recipientPhone: null,
    });
  });

  it('自取：不送', () => {
    expect(checkoutRecipientPayload('SelfPickup', '王小美', '0912345678')).toEqual({
      recipientName: null,
      recipientPhone: null,
    });
  });

  it('還沒選配送方式：不送', () => {
    expect(checkoutRecipientPayload(null, '王小美', '0912345678')).toEqual({
      recipientName: null,
      recipientPhone: null,
    });
  });
});
