import { orders } from '@greygray/api-client/mock/fixtures.storefront';
import { orderLineStatusLabel } from '../../_lib/orderStatus';
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { OrderSummary } from '../_components/OrderSummary';

(globalThis as unknown as { React: typeof React }).React = React;

const order = orders.find((candidate) => candidate.paidAmount != null);

if (!order) throw new Error('訂單 fixture 不可為空');

describe('OrderSummary', () => {
  const detailHtml = renderToStaticMarkup(<OrderSummary order={order} variant="detail" />);
  const confirmationHtml = renderToStaticMarkup(<OrderSummary order={order} variant="confirmation" />);

  it('T5 detail：Fragment 直接輸出兩張原 class 卡片', () => {
    const cardClasses = [...detailHtml.matchAll(/class="([^"]*rounded-card[^"]*)"/g)].map((match) => match[1]);
    expect(cardClasses).toEqual([
      'rounded-card bg-surface shadow-card p-[var(--gg-space-5)] flex flex-col gap-[var(--gg-space-4)]',
      'rounded-card bg-surface shadow-card p-[var(--gg-space-5)] flex flex-col gap-[var(--gg-space-2)]',
    ]);
  });

  it('T5 detail：金額順序不變', () => {
    const labels = ['商品小計', '運費', '含運總額', '已付金額'];
    const positions = labels.map((label) => detailHtml.indexOf(label));
    expect(positions.every((position) => position >= 0)).toBe(true);
    expect(positions).toEqual([...positions].sort((left, right) => left - right));
  });

  it('T5 detail：保留狀態標籤且沒有縮圖', () => {
    expect(detailHtml).toContain(orderLineStatusLabel(order.lines[0]!.status));
    expect(detailHtml).not.toContain('h-12 w-12');
  });

  it('T5 confirmation：有無圖佔位與 Badge 元素，不顯示品項狀態', () => {
    expect(confirmationHtml).toContain('無圖');
    expect(confirmationHtml).toContain('inline-flex items-center rounded-pill');
    expect(confirmationHtml).not.toContain(orderLineStatusLabel(order.lines[0]!.status));
  });
});
