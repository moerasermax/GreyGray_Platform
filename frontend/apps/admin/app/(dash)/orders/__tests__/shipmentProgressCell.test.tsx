/**
 * 訂單列表「出貨進度」欄（#49）的呈現層。文案本身已經在
 * `../_lib/shipmentProgress.test.ts` 逐條驗過，這裡只確認元件把它畫進 `<td>`，
 * 而且在「出貨單清單讀取失敗」（`shipments === null`）時仍然畫得出這一格，
 * 不會讓整張表格連其他欄位都出不來（做法照 `../../__tests__/dashboardLedger.test.tsx` 檔頭）。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import { ShipmentProgressCell } from '../_components/ShipmentProgressCell';

(globalThis as unknown as { React: typeof React }).React = React;

type S = components['schemas'];

const ORDER_ID = '0199f0c2-0000-7000-8000-0000000000a1';

function shipment(
  id: string,
  status: S['ShipmentStatus'],
  extra: Partial<S['AdminShipment']> = {},
): S['AdminShipment'] {
  return {
    id,
    method: 'HomeDelivery',
    status,
    trackingNumber: null,
    orderIds: [ORDER_ID],
    carrierCost: null,
    dispatchedAt: null,
    deliveredAt: null,
    ...extra,
  };
}

function renderCell(props: Partial<React.ComponentProps<typeof ShipmentProgressCell>> = {}): string {
  return renderToStaticMarkup(
    <table>
      <tbody>
        <tr>
          <ShipmentProgressCell
            orderId={ORDER_ID}
            orderStatus="ReadyToShip"
            shipments={[]}
            truncated={false}
            {...props}
          />
        </tr>
      </tbody>
    </table>,
  );
}

describe('訂單列表「出貨進度」欄（#49）', () => {
  it('沒有出貨單時畫「未建立」', () => {
    expect(renderCell()).toContain('未建立');
  });

  it('★ 部分簽收：畫得出張數與還差幾張', () => {
    const html = renderCell({
      shipments: [
        shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' }),
        shipment('s2', 'Draft'),
      ],
    });

    expect(html).toContain('2 張');
    expect(html).toContain('還差 1 張簽收');
  });

  it('★ 已取消不顯示誤導的出貨進度', () => {
    const html = renderCell({
      orderStatus: 'Cancelled',
      shipments: [shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' })],
    });

    expect(html).toContain('已取消');
    expect(html).not.toContain('已簽收');
  });

  it('★ 出貨單清單讀取失敗時仍然畫得出這一格（不擋整列其他欄位）', () => {
    const html = renderCell({ shipments: null });

    expect(html).toContain('讀取失敗');
  });
});
