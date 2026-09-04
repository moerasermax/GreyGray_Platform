/**
 * 訂單頁「出貨單」區塊的文案（#45）。
 *
 * 沒有 jsdom、沒有 @testing-library，用 `renderToStaticMarkup` 把純呈現元件
 * 畫成 HTML 字串再斷言（做法與理由見 `../../__tests__/dashboardLedger.test.tsx` 檔頭）。
 * JSX 走 classic transform，所以要把 `React` 掛到 global 上。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import { OrderShipmentsSection } from '../_components/OrderShipmentsSection';

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

/** 正式機那三張（派工書 §0.1）。 */
const LIVE_THREE: readonly S['AdminShipment'][] = [
  shipment('01a0682d-0000-7000-8000-000000000001', 'Draft'),
  shipment('01a0682e-0000-7000-8000-000000000002', 'Delivered', {
    trackingNumber: 'HD-0001',
    dispatchedAt: '2026-09-04T00:50:52Z',
    deliveredAt: '2026-09-04T00:51:02Z',
  }),
  shipment('01a0682e-0000-7000-8000-000000000003', 'Dispatched', {
    method: 'ConvenienceStore',
    dispatchedAt: '2026-09-04T00:51:29Z',
  }),
];

function render(props: Partial<React.ComponentProps<typeof OrderShipmentsSection>> = {}): string {
  return renderToStaticMarkup(
    <OrderShipmentsSection
      orderId={ORDER_ID}
      shipments={[]}
      loading={false}
      loadFailed={false}
      truncated={false}
      onRetry={() => {}}
      {...props}
    />,
  );
}

describe('訂單頁的出貨單區塊', () => {
  it('★ 部分簽收：講得出還差幾張，並說明全部簽收才會轉已出貨', () => {
    const html = render({ shipments: LIVE_THREE });

    expect(html).toContain('3 張出貨單');
    expect(html).toContain('還有 2 張沒有簽收');
    expect(html).toContain('全部簽收後訂單才會轉為已出貨');
  });

  it('★ 沒有出貨單時不是一片空白', () => {
    const html = render({ shipments: [] });

    expect(html).toContain('還沒有建立出貨單');
  });

  it('列出每一張：狀態標籤、配送方式、追蹤單號、交運與送達', () => {
    const html = render({ shipments: LIVE_THREE });

    // 標籤與 tone 來自 shipments/_lib/labels.ts，這裡順便釘住沒有被重寫成別的字。
    expect(html).toContain('草稿');
    expect(html).toContain('已送達');
    expect(html).toContain('已交運');
    expect(html).toContain('宅配到府');
    expect(html).toContain('超商取貨');
    expect(html).toContain('HD-0001');
    expect(html).toContain('尚未交運');
    expect(html).toContain('尚未送達');
    // 可以點進出貨單詳情。
    expect(html).toContain('/shipments/01a0682d-0000-7000-8000-000000000001');
  });

  it('別張訂單的出貨單不會算進來', () => {
    const html = render({
      shipments: [shipment('s-other', 'Delivered', { orderIds: ['別人的訂單'] })],
    });

    expect(html).toContain('還沒有建立出貨單');
  });

  it('全部簽收時講「全部都已簽收」', () => {
    const html = render({
      shipments: [shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' })],
    });

    expect(html).toContain('全部都已簽收');
    expect(html).not.toContain('全部簽收後訂單才會轉為已出貨');
  });

  it('讀失敗時只有這一區塊講失敗，不畫任何張數', () => {
    const html = render({ loadFailed: true, shipments: LIVE_THREE });

    expect(html).toContain('讀取出貨單失敗');
    expect(html).toContain('重試');
    expect(html).not.toContain('張出貨單');
  });

  it('載入中不先講一個會變的張數', () => {
    const html = render({ loading: true, shipments: LIVE_THREE });

    expect(html).toContain('讀取出貨單中');
    expect(html).not.toContain('張出貨單');
  });

  it('還有下一頁時明講張數可能不完整，不假裝是總數', () => {
    const html = render({ shipments: LIVE_THREE, truncated: true });

    expect(html).toContain('張數可能不完整');
  });
});
