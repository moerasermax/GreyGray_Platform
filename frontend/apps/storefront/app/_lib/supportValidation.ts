/**
 * 客服留言表單的前端驗證——**先擋，不等後端 422**（派工書 §3.3）。
 * 純函式，跟元件分開才測得到（這個 workspace 沒有 jsdom）。
 */

/** 契約 `message` 上限（`docs/37` §0.3）。 */
export const SUPPORT_MESSAGE_MAX_LENGTH = 2000;

export function isSupportMessageValid(message: string): boolean {
  const trimmed = message.trim();
  return trimmed.length > 0 && trimmed.length <= SUPPORT_MESSAGE_MAX_LENGTH;
}

/** email 與 phone 至少一個（契約明文要求）。 */
export function isSupportContactValid(email: string, phone: string): boolean {
  return email.trim() !== '' || phone.trim() !== '';
}

export function canSubmitSupportTicket(message: string, email: string, phone: string): boolean {
  return isSupportMessageValid(message) && isSupportContactValid(email, phone);
}

/** `null` 代表沒有錯誤。 */
export function supportMessageLengthError(message: string): string | null {
  const trimmed = message.trim();
  if (trimmed.length === 0) return '請填寫想告訴我們的內容。';
  if (trimmed.length > SUPPORT_MESSAGE_MAX_LENGTH) {
    return `訊息不能超過 ${SUPPORT_MESSAGE_MAX_LENGTH} 字（目前 ${trimmed.length} 字）。`;
  }
  return null;
}

export function supportContactError(email: string, phone: string): string | null {
  if (isSupportContactValid(email, phone)) return null;
  return '請至少留下 email 或手機號碼，我們才能回覆您。';
}
