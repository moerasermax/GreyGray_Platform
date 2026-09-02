/**
 * 付款結果頁自動重查的排程測試。
 *
 * 這一支要擋的不是「重查會不會動」，而是**重查會不會停**。
 * 無限輪詢的失效方式很安靜：正常情況下完全看不出來，只有在付款真的失敗
 * 或 Worker 掛掉時才會變成一個永遠轉不完的畫面 ＋ 一條打不完的請求。
 */
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/storefront';
import {
  POLL_DELAYS_MS,
  POLL_TOTAL_MS,
  pollDelayMs,
  shouldKeepPolling,
} from '../paymentResultPolling';

type OrderStatus = components['schemas']['OrderStatus'];

/** 契約裡的九個 `OrderStatus` 扣掉 `AwaitingPayment`，其餘八個全列進來，一個都不漏。 */
const NON_POLLING_STATUSES: OrderStatus[] = [
  'PaidAwaitingClose',
  'ClosedAwaitingDeparture',
  'Purchasing',
  'GoodsReceived',
  'ReadyToShip',
  'Shipped',
  'Completed',
  'Cancelled',
];

describe('排程是有限的', () => {
  it('★ 次數有上限——沒有這一條就是無限輪詢', () => {
    expect(POLL_DELAYS_MS.length).toBeGreaterThan(0);
    expect(POLL_DELAYS_MS.length).toBeLessThanOrEqual(8);
  });

  it('★ 排到最後一次之後就停，不管狀態還是不是 AwaitingPayment', () => {
    expect(shouldKeepPolling('AwaitingPayment', POLL_DELAYS_MS.length - 1)).toBe(true);
    expect(shouldKeepPolling('AwaitingPayment', POLL_DELAYS_MS.length)).toBe(false);
    expect(shouldKeepPolling('AwaitingPayment', POLL_DELAYS_MS.length + 99)).toBe(false);
  });

  it('間隔遞增——剛回來的那一兩秒最可能已經好了，之後不要一直壓後端', () => {
    for (let i = 1; i < POLL_DELAYS_MS.length; i += 1) {
      expect(POLL_DELAYS_MS[i]!).toBeGreaterThan(POLL_DELAYS_MS[i - 1]!);
    }
  });

  it('每一次都是正的毫秒數——0 或負數等於同步爆打', () => {
    for (const delay of POLL_DELAYS_MS) {
      expect(delay).toBeGreaterThan(0);
    }
  });

  it('整段排程在半分鐘內結束，盯著看得完', () => {
    expect(POLL_TOTAL_MS).toBe(POLL_DELAYS_MS.reduce((sum, d) => sum + d, 0));
    expect(POLL_TOTAL_MS).toBeLessThanOrEqual(30_000);
  });
});

describe('只有 AwaitingPayment 才重查', () => {
  it.each(NON_POLLING_STATUSES.map((status) => [status]))(
    '%s 一次都不重查——後端已經給了答案，再問是同一個答案',
    (status) => {
      expect(shouldKeepPolling(status, 0)).toBe(false);
    },
  );

  it('AwaitingPayment 從第 0 次開始重查', () => {
    expect(shouldKeepPolling('AwaitingPayment', 0)).toBe(true);
  });

  it('狀態在半路變了就停——不是「排程跑完才看狀態」', () => {
    expect(shouldKeepPolling('AwaitingPayment', 2)).toBe(true);
    expect(shouldKeepPolling('PaidAwaitingClose', 2)).toBe(false);
  });
});

describe('pollDelayMs', () => {
  it('每一次都對得到排程裡的那個值', () => {
    POLL_DELAYS_MS.forEach((delay, index) => {
      expect(pollDelayMs(index)).toBe(delay);
    });
  });

  it('★ 排程用完回 null，不是回最後一個值——呼叫端要靠這個分辨「不要再等了」', () => {
    expect(pollDelayMs(POLL_DELAYS_MS.length)).toBeNull();
    expect(pollDelayMs(POLL_DELAYS_MS.length + 1)).toBeNull();
  });

  it('負數與非整數都回 null，不猜', () => {
    expect(pollDelayMs(-1)).toBeNull();
    expect(pollDelayMs(1.5)).toBeNull();
    expect(shouldKeepPolling('AwaitingPayment', -1)).toBe(false);
    expect(shouldKeepPolling('AwaitingPayment', 1.5)).toBe(false);
  });
});
