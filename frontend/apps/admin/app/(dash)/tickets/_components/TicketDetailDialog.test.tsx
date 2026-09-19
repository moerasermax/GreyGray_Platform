/**
 * 沒有 jsdom，用 `renderToStaticMarkup` 測第一次渲染的畫面（做法見
 * `apps/admin/app/(dash)/__tests__/dashboardLedger.test.tsx` 檔頭）。
 * `Dialog`（`@greygray/ui/admin`）不像前台那支用 `createPortal`，
 * 是普通的 `fixed` 定位，SSR 量得到。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { SupportTicket } from '../_lib/api';
import { TicketDetailDialog } from './TicketDetailDialog';

(globalThis as unknown as { React: typeof React }).React = React;

const OPEN_TICKET: SupportTicket = {
  id: 'ticket-1',
  status: 'open',
  message: '請問我的訂單什麼時候會出貨？',
  contactEmail: 'customer@example.com',
  contactPhone: '0912345678',
  menuPath: ['運送與取貨', '現貨付款後多久出貨？'],
  orderId: 'order-1',
  customerId: 'customer-1',
  createdAt: '2026-09-19T00:00:00Z',
  resolvedAt: null,
  resolvedBy: null,
  staffNote: null,
};

const RESOLVED_TICKET: SupportTicket = {
  ...OPEN_TICKET,
  status: 'resolved',
  resolvedAt: '2026-09-19T01:00:00Z',
  resolvedBy: 'staff-1',
  staffNote: '已回覆客人，出貨中。',
};

const NOOP = () => {};
const NOOP_ASYNC = async () => {};

describe('open 工單、有 resolve 權限：看得到「標記已處理」', () => {
  it('畫出留言、聯絡方式、選單路徑，以及「標記已處理」按鈕', () => {
    const html = renderToStaticMarkup(
      <TicketDetailDialog
        open
        ticket={OPEN_TICKET}
        onClose={NOOP}
        onResolve={NOOP_ASYNC}
        onConflict={NOOP}
        canResolve
      />,
    );
    expect(html).toContain('請問我的訂單什麼時候會出貨？');
    expect(html).toContain('customer@example.com');
    expect(html).toContain('0912345678');
    expect(html).toContain('運送與取貨 › 現貨付款後多久出貨？');
    expect(html).toContain('標記已處理');
    expect(html).toContain('未結案');
  });
});

describe('open 工單、沒有 resolve 權限（ReadOnly）：看不到「標記已處理」', () => {
  it('不畫出標記已處理按鈕與處理備註輸入框', () => {
    const html = renderToStaticMarkup(
      <TicketDetailDialog
        open
        ticket={OPEN_TICKET}
        onClose={NOOP}
        onResolve={NOOP_ASYNC}
        onConflict={NOOP}
        canResolve={false}
      />,
    );
    expect(html).not.toContain('標記已處理');
  });
});

describe('resolved 工單：顯示處理備註，不再顯示送出用的欄位', () => {
  it('畫出處理備註內容', () => {
    const html = renderToStaticMarkup(
      <TicketDetailDialog
        open
        ticket={RESOLVED_TICKET}
        onClose={NOOP}
        onResolve={NOOP_ASYNC}
        onConflict={NOOP}
        canResolve
      />,
    );
    expect(html).toContain('已結案');
    expect(html).toContain('已回覆客人，出貨中。');
    expect(html).not.toContain('標記已處理');
  });
});

describe('open=false 或 ticket=null：不畫任何東西', () => {
  it('open=false', () => {
    const html = renderToStaticMarkup(
      <TicketDetailDialog open={false} ticket={OPEN_TICKET} onClose={NOOP} onResolve={NOOP_ASYNC} onConflict={NOOP} canResolve />,
    );
    expect(html).toBe('');
  });

  it('ticket=null（例如列表還沒選到任何一列）', () => {
    const html = renderToStaticMarkup(
      <TicketDetailDialog open ticket={null} onClose={NOOP} onResolve={NOOP_ASYNC} onConflict={NOOP} canResolve />,
    );
    expect(html).toBe('');
  });
});
