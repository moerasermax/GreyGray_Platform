/**
 * 客服留言的錯誤與送出後文案。
 *
 * ⚠ 目前站上沒有任何已拍板的回覆時效（客服聯絡方式本身都還沒定，
 * 見 `GreyGray_PM` 的 blocker），所以這裡**不編一個假的工作天數**——
 * FAQ 三處既有文案已經用「我們會儘快回覆」當全站唯一說法（`(info)/_content/faq.ts`），
 * 這裡沿用同一句，不是漏做 #7「要告訴客人多久會回」，是刻意不虛構一個沒人拍過板的數字。
 */
import { ApiError } from '@greygray/api-client';

export const SUPPORT_WAIT_MESSAGE =
  '已收到您的留言，我們會儘快回覆。若您留了 email 或手機，我們會直接聯絡您，請留意通知。';

/** 419／429 的「你剛剛已經留過言了」——文案照派工書 §1.1 第 8 條給的原句。 */
export const SUPPORT_RATE_LIMITED_MESSAGE = '你剛剛已經留過言了，我們會儘快回覆。';

const SUPPORT_CONTACT_REQUIRED_MESSAGE = '請至少留下 email 或手機號碼，我們才能回覆您。';

/** 把送出失敗轉成看得懂的中文，不顯示 `ApiError` 的技術細節（契約 `code` 見 §0.3）。 */
export function supportSubmitErrorMessage(cause: unknown): string {
  if (cause instanceof ApiError) {
    if (cause.is('support.too-many-requests')) return SUPPORT_RATE_LIMITED_MESSAGE;
    if (cause.is('support.contact-required')) return SUPPORT_CONTACT_REQUIRED_MESSAGE;
    return cause.problem.detail ? `${cause.problem.title}（${cause.problem.detail}）` : cause.problem.title;
  }
  return '留言送出失敗，請稍後再試一次。';
}
