import { describe, expect, it } from 'vitest';
import { fromDatetimeLocalValue, toDatetimeLocalValue } from './datetime';

/**
 * 這一組守的是**截團時間不可以在來回轉換之間位移**。
 *
 * 情境：營運人員開啟一個既有的團、只改了商品或售價、按儲存。
 * 表單載入時 `closesAt` 走 `toDatetimeLocalValue` 填進
 * `<input type="datetime-local">`，送出時再走 `fromDatetimeLocalValue` 轉回去。
 * **他沒有碰日期欄位**，所以送回後端的時間必須跟原本一模一樣。
 *
 * 這裡刻意不寫死任何字串比對——測試會在開發機、CI 與正式機的不同時區跑，
 * 寫死時區的測試只會在某一台過。改測「同一個瞬間」這個不變式。
 */

/** 取到分鐘為止的瞬間（`datetime-local` 沒有秒，所以基準也不能有秒）。 */
function instant(iso: string): number {
  return new Date(iso).getTime();
}

describe('截團時間的來回轉換', () => {
  it('round-trip 不位移——這是最重要的一條', () => {
    const original = '2026-09-15T20:30:00+08:00';
    const roundTripped = fromDatetimeLocalValue(toDatetimeLocalValue(original));
    expect(instant(roundTripped)).toBe(instant(original));
  });

  it('跨月、跨年、月底都不位移', () => {
    for (const iso of [
      '2026-01-01T00:00:00+08:00',
      '2026-02-28T23:59:00+08:00',
      '2026-12-31T23:59:00+08:00',
      '2027-03-01T00:01:00+08:00',
    ]) {
      expect(instant(fromDatetimeLocalValue(toDatetimeLocalValue(iso))), iso).toBe(instant(iso));
    }
  });

  it('連續轉換兩次仍然不漂移——重複編輯同一個團不會愈跑愈偏', () => {
    const original = '2026-09-15T20:30:00+08:00';
    const once = fromDatetimeLocalValue(toDatetimeLocalValue(original));
    const twice = fromDatetimeLocalValue(toDatetimeLocalValue(once));
    expect(instant(twice)).toBe(instant(original));
  });
});

describe('toDatetimeLocalValue 的輸出格式', () => {
  it('是 <input type="datetime-local"> 吃得下的形狀，且不帶時區位移', () => {
    const value = toDatetimeLocalValue('2026-09-15T20:30:00+08:00');
    expect(value).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
  });

  it('個位數的月日時分要補零，否則 input 讀不到值', () => {
    const value = toDatetimeLocalValue('2026-01-02T03:04:00+08:00');
    expect(value).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
    expect(value).not.toContain('T3:');
  });
});

describe('fromDatetimeLocalValue 的輸出格式', () => {
  it('符合契約要求的 ISO 8601 含位移（docs/05 §6）', () => {
    const iso = fromDatetimeLocalValue('2026-09-15T20:30');
    expect(iso).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$/);
  });

  it('產出的字串 Date 解得出來，而且就是輸入的那個本地時間', () => {
    const parsed = new Date(fromDatetimeLocalValue('2026-09-15T20:30'));
    expect(parsed.getFullYear()).toBe(2026);
    expect(parsed.getMonth()).toBe(8);
    expect(parsed.getDate()).toBe(15);
    expect(parsed.getHours()).toBe(20);
    expect(parsed.getMinutes()).toBe(30);
  });
});
