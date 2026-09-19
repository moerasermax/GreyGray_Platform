import { describe, expect, it } from 'vitest';
import {
  SUPPORT_MESSAGE_MAX_LENGTH,
  canSubmitSupportTicket,
  isSupportContactValid,
  isSupportMessageValid,
  supportContactError,
  supportMessageLengthError,
} from '../supportValidation';

describe('isSupportMessageValid', () => {
  it('空字串（含只有空白）不合法', () => {
    expect(isSupportMessageValid('')).toBe(false);
    expect(isSupportMessageValid('   ')).toBe(false);
  });

  it('1 個字合法', () => {
    expect(isSupportMessageValid('嗨')).toBe(true);
  });

  it(`剛好 ${SUPPORT_MESSAGE_MAX_LENGTH} 字合法`, () => {
    expect(isSupportMessageValid('a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH))).toBe(true);
  });

  it(`超過 ${SUPPORT_MESSAGE_MAX_LENGTH} 字擋下`, () => {
    expect(isSupportMessageValid('a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH + 1))).toBe(false);
  });
});

describe('isSupportContactValid：email 與手機至少一個', () => {
  it('兩個都沒填 → 不合法', () => {
    expect(isSupportContactValid('', '')).toBe(false);
    expect(isSupportContactValid('  ', '  ')).toBe(false);
  });

  it('只填 email → 合法', () => {
    expect(isSupportContactValid('a@b.com', '')).toBe(true);
  });

  it('只填手機 → 合法', () => {
    expect(isSupportContactValid('', '0912345678')).toBe(true);
  });

  it('兩個都填 → 合法', () => {
    expect(isSupportContactValid('a@b.com', '0912345678')).toBe(true);
  });
});

describe('canSubmitSupportTicket', () => {
  it('訊息與聯絡方式都合法才能送出', () => {
    expect(canSubmitSupportTicket('嗨', 'a@b.com', '')).toBe(true);
  });

  it('訊息空白 → 擋下', () => {
    expect(canSubmitSupportTicket('', 'a@b.com', '')).toBe(false);
  });

  it('email 與手機都沒填 → 擋下（派工書 §3.3 明列的情況）', () => {
    expect(canSubmitSupportTicket('嗨', '', '')).toBe(false);
  });

  it(`訊息超過 ${SUPPORT_MESSAGE_MAX_LENGTH} 字 → 擋下（派工書 §3.3 明列的情況）`, () => {
    expect(canSubmitSupportTicket('a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH + 1), 'a@b.com', '')).toBe(false);
  });
});

describe('supportMessageLengthError', () => {
  it('合法訊息回 null', () => {
    expect(supportMessageLengthError('嗨')).toBeNull();
  });

  it('空白回錯誤訊息', () => {
    expect(supportMessageLengthError('')).not.toBeNull();
  });

  it('超長訊息回錯誤訊息，且報出目前字數', () => {
    const message = 'a'.repeat(SUPPORT_MESSAGE_MAX_LENGTH + 5);
    const error = supportMessageLengthError(message);
    expect(error).not.toBeNull();
    expect(error).toContain(String(SUPPORT_MESSAGE_MAX_LENGTH));
  });
});

describe('supportContactError', () => {
  it('至少一項有填 → null', () => {
    expect(supportContactError('a@b.com', '')).toBeNull();
  });

  it('兩項都沒填 → 有錯誤訊息', () => {
    expect(supportContactError('', '')).not.toBeNull();
  });
});
