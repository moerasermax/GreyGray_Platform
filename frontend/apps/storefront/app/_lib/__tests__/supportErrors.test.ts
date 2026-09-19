import { ApiError } from '@greygray/api-client';
import { describe, expect, it } from 'vitest';
import { SUPPORT_RATE_LIMITED_MESSAGE, supportSubmitErrorMessage } from '../supportErrors';

function problem(code: string, title: string, status: number) {
  return new ApiError({ type: `https://greygray.tw/errors/${code}`, title, status, code });
}

describe('supportSubmitErrorMessage', () => {
  it('429（support.too-many-requests）→ 看得懂的「你剛剛已經留過言了」，不是技術錯誤', () => {
    const cause = problem('support.too-many-requests', 'Too Many Requests', 429);
    expect(supportSubmitErrorMessage(cause)).toBe(SUPPORT_RATE_LIMITED_MESSAGE);
    // 不能把原始 title 洩漏出去給客人看
    expect(supportSubmitErrorMessage(cause)).not.toContain('Too Many Requests');
  });

  it('422（support.contact-required）→ 提示至少留一種聯絡方式', () => {
    const cause = problem('support.contact-required', 'Contact Required', 422);
    expect(supportSubmitErrorMessage(cause)).toContain('email');
  });

  it('其他 ApiError → 用後端給的 title（可以直接顯示給客人）', () => {
    const cause = problem('platform.unexpected', '系統發生問題，請稍後再試。', 500);
    expect(supportSubmitErrorMessage(cause)).toBe('系統發生問題，請稍後再試。');
  });

  it('非 ApiError（例如網路錯誤）→ 通用訊息，不會是 undefined 或拋例外', () => {
    expect(supportSubmitErrorMessage(new Error('boom'))).toBe('留言送出失敗，請稍後再試一次。');
    expect(supportSubmitErrorMessage('boom')).toBe('留言送出失敗，請稍後再試一次。');
  });
});
