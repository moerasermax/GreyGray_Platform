import { describe, expect, it } from 'vitest';
import { ORDER_MAIN_LINE, buildOrderTimeline, orderLineStatusLabel, orderStatusLabel } from './orderStatus';

// 九個訂單狀態（`docs/api/openapi.storefront.yaml` 的 OrderStatus enum）。
const ALL_ORDER_STATUSES = [
  'AwaitingPayment',
  'PaidAwaitingClose',
  'ClosedAwaitingDeparture',
  'Purchasing',
  'GoodsReceived',
  'ReadyToShip',
  'Shipped',
  'Completed',
  'Cancelled',
] as const;

describe('buildOrderTimeline', () => {
  it.each(ALL_ORDER_STATUSES)('九個狀態都能畫出時間軸：%s', (status) => {
    const timeline = buildOrderTimeline(status);
    expect(timeline.steps).toHaveLength(ORDER_MAIN_LINE.length);
    expect(timeline.rawStatus).toBe(status);
    // 每一步都要有非空白的中文標籤，不能是 undefined 或空字串。
    for (const step of timeline.steps) {
      expect(step.label.length).toBeGreaterThan(0);
    }
  });

  it('Cancelled 不在主線上，標成 isCancelled 而不是走到某一步', () => {
    const timeline = buildOrderTimeline('Cancelled');
    expect(timeline.isCancelled).toBe(true);
    expect(timeline.steps.every((s) => s.state === 'upcoming')).toBe(true);
  });

  it('依狀態算出正確的 done/current/upcoming', () => {
    const timeline = buildOrderTimeline('Purchasing');
    expect(timeline.steps.map((s) => s.state)).toEqual([
      'done', // AwaitingPayment
      'done', // PaidAwaitingClose
      'done', // ClosedAwaitingDeparture
      'current', // Purchasing
      'upcoming', // GoodsReceived
      'upcoming', // ReadyToShip
      'upcoming', // Shipped
      'upcoming', // Completed
    ]);
  });

  it('未知狀態不會崩掉，畫面顯示原始字串', () => {
    const unknown = 'SomeFutureStatusNotInThisBuild';
    const timeline = buildOrderTimeline(unknown);
    expect(timeline.isCancelled).toBe(false);
    expect(timeline.rawStatus).toBe(unknown);
    expect(timeline.steps.every((s) => s.state === 'upcoming')).toBe(true);
    expect(orderStatusLabel(unknown)).toBe(unknown);
  });
});

describe('orderLineStatusLabel', () => {
  it('已知的 line 狀態都有中文標籤', () => {
    const known = ['Pending', 'Reserved', 'Purchased', 'Unavailable', 'Shipped', 'Completed', 'Cancelled'];
    for (const status of known) {
      expect(orderLineStatusLabel(status)).not.toBe(status);
    }
  });

  it('未知的 line 狀態退回顯示原始字串', () => {
    expect(orderLineStatusLabel('SomeNewLineStatus')).toBe('SomeNewLineStatus');
  });
});
