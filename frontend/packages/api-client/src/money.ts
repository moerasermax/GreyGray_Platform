/**
 * 金額。**這是前端唯一被允許碰金額的地方。**
 *
 * 契約見 `docs/05-API契約.md` §6：後端一律回 `{ amountMinor, currency }`，
 * `amountMinor` 是最小單位——TWD 的最小單位是分，`NT$180` 就是 `18000`。
 *
 * 規則只有一條，但沒有例外：
 * **前端不做金額運算，只做顯示。** 加總、分攤、折扣、含運總額全部由後端算好回傳。
 * 元件裡出現 `a.amountMinor + b.amountMinor` 就是 bug——
 * 帳一旦有兩個來源，其中一個永遠沒有測試。
 */

/** 與後端 `GreyGray.Shared.Kernel.Currency` 一一對應。 */
export type Currency =
  | 'TWD'
  | 'JPY'
  | 'USD'
  | 'KRW'
  | 'EUR'
  | 'HKD'
  | 'CNY'
  | 'THB'
  | 'GBP'
  | 'SGD';

export interface Money {
  /** 最小單位的整數。TWD：18000 = NT$180。 */
  readonly amountMinor: number;
  readonly currency: Currency;
}

/**
 * 各幣別最小單位的小數位數（ISO 4217 exponent）。
 * **JPY 與 KRW 沒有小數位**——這是「金額一律用整數最小單位」時最容易踩到的坑：
 * 把日圓當成兩位小數，¥1000 會被顯示成 ¥10。
 */
const MINOR_UNIT_DIGITS: Record<Currency, number> = {
  TWD: 2,
  JPY: 0,
  KRW: 0,
  USD: 2,
  EUR: 2,
  HKD: 2,
  CNY: 2,
  THB: 2,
  GBP: 2,
  SGD: 2,
};

export function minorUnitDigits(currency: Currency): number {
  return MINOR_UNIT_DIGITS[currency] ?? 2;
}

const formatterCache = new Map<string, Intl.NumberFormat>();

/**
 * ADR-028：台幣顯示統一加 `NT$` 前綴。
 *
 * `zh-TW` locale 對 TWD 的 ICU 行為是不帶國別前綴的 `$`（本地語系的正確行為，不是 bug）。
 * `en-US` locale 對 TWD 剛好會輸出 `NT$` 前綴、千分位與小數點分隔符號與 `zh-TW` 相同，
 * 所以只有 TWD 換 locale，其餘幣別維持 `zh-TW`（`US$`、`HK$` 這些既有前綴不變）。
 */
function getFormatter(currency: Currency, showDecimals: boolean): Intl.NumberFormat {
  const key = `${currency}:${showDecimals}`;
  let formatter = formatterCache.get(key);
  if (!formatter) {
    const digits = showDecimals ? minorUnitDigits(currency) : 0;
    const locale = currency === 'TWD' ? 'en-US' : 'zh-TW';
    formatter = new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    });
    formatterCache.set(key, formatter);
  }
  return formatter;
}

export interface FormatMoneyOptions {
  /**
   * 是否顯示小數。預設 `false`。
   *
   * 台幣的日常金額沒有分——顯示 `NT$180` 而不是 `NT$180.00`。
   * 但**帳務畫面（後台的分錄、對帳）要設成 `true`**，因為那裡的一分錢差異就是要查的東西。
   */
  /*
   * `| undefined` 是必要的，不是贅字：tsconfig 開了 exactOptionalPropertyTypes，
   * 少了它，呼叫端傳一個型別為 `boolean | undefined` 的變數進來會編譯失敗。
   */
  readonly showDecimals?: boolean | undefined;
}

/**
 * 把 `Money` 轉成畫面上的字串。
 *
 * ```ts
 * formatMoney({ amountMinor: 18000, currency: 'TWD' })  // "NT$180"
 * formatMoney({ amountMinor: 18050, currency: 'TWD' }, { showDecimals: true })  // "NT$180.50"
 * formatMoney({ amountMinor: 1000, currency: 'JPY' })   // "¥1,000"  ← 不是 ¥10
 * formatMoney({ amountMinor: 78000, currency: 'USD' })  // "US$780"
 * ```
 *
 * **台幣統一顯示 `NT$`（ADR-028，2026-08-30）。** `zh-TW` locale 對 TWD 的 ICU 行為
 * 本來是不帶前綴的 `$`（本地語系的正確行為，不是 bug），但老闆決定統一成 `NT$`；
 * 外幣的既有前綴（`US$`、`HK$`）不受影響。實作只換了 TWD 的 formatter locale，
 * 不要在呼叫端自己加前綴。
 */
export function formatMoney(money: Money, options: FormatMoneyOptions = {}): string {
  const digits = minorUnitDigits(money.currency);
  const major = money.amountMinor / 10 ** digits;
  return getFormatter(money.currency, options.showDecimals ?? false).format(major);
}

/**
 * 把金額輸入框的「主單位字串」精確轉成契約使用的整數最小單位。
 *
 * 這是寫入表單唯一可以做主單位／最小單位換算的地方；不用浮點乘法，避免
 * `10.29 * 100` 之類的 IEEE-754 誤差。格式不合法、超過幣別小數位數或超過
 * JavaScript 安全整數範圍時回 `null`，由表單顯示驗證訊息。
 */
export function moneyFromMajorInput(value: string, currency: Currency): Money | null {
  const normalized = value.trim().replaceAll(',', '');
  const match = /^(-?)(\d+)(?:\.(\d*))?$/.exec(normalized);
  if (!match) return null;

  const [, sign, whole = '', fraction = ''] = match;
  const digits = minorUnitDigits(currency);
  if (fraction.length > digits) return null;

  const factor = 10 ** digits;
  const fractionMinor = fraction ? Number(fraction.padEnd(digits, '0')) : 0;
  const unsignedMinor = Number(whole) * factor + fractionMinor;
  if (!Number.isSafeInteger(unsignedMinor)) return null;

  return {
    amountMinor: sign === '-' ? -unsignedMinor : unsignedMinor,
    currency,
  };
}

/** 把契約金額轉成適合 `<input type="number">` 的無幣別字串。 */
export function moneyToMajorInput(money: Money): string {
  const digits = minorUnitDigits(money.currency);
  if (digits === 0) return String(money.amountMinor);

  const factor = 10 ** digits;
  const sign = money.amountMinor < 0 ? '-' : '';
  const unsignedMinor = Math.abs(money.amountMinor);
  const whole = Math.floor(unsignedMinor / factor);
  const fraction = String(unsignedMinor % factor).padStart(digits, '0').replace(/0+$/, '');
  return `${sign}${whole}${fraction ? `.${fraction}` : ''}`;
}

/** 只要數字不要幣別符號，例如放在已經標了幣別的表格欄位裡。 */
export function formatAmount(money: Money, options: FormatMoneyOptions = {}): string {
  // 換算一定用該幣別真正的小數位數；顯示幾位才看 showDecimals。
  // 兩者混用的話，JPY 會被除以 100 而顯示成原本的百分之一。
  const major = money.amountMinor / 10 ** minorUnitDigits(money.currency);
  const displayDigits = (options.showDecimals ?? false) ? minorUnitDigits(money.currency) : 0;

  return new Intl.NumberFormat('zh-TW', {
    minimumFractionDigits: displayDigits,
    maximumFractionDigits: displayDigits,
  }).format(major);
}

export function isZero(money: Money): boolean {
  return money.amountMinor === 0;
}

export function isNegative(money: Money): boolean {
  return money.amountMinor < 0;
}

/**
 * 比大小。**只有比較，沒有加減乘除**——那些是後端的事。
 * 跨幣別比較直接丟例外，跟後端 `Money.EnsureSameCurrency` 的行為一致。
 */
export function compareMoney(a: Money, b: Money): number {
  if (a.currency !== b.currency) {
    throw new Error(
      `不可混算幣別：${a.currency} 與 ${b.currency}。前端不做換匯，也不做金額運算。`,
    );
  }
  return a.amountMinor - b.amountMinor;
}
