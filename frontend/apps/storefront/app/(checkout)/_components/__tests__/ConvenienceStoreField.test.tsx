/**
 * 選門市元件（ADR-038）的靜態渲染。
 *
 * 沒有 jsdom，所以用 `renderToStaticMarkup`（做法與理由見
 * `apps/admin/app/(dash)/__tests__/dashboardLedger.test.tsx` 檔頭）。
 * 按鈕的鎖、自動送出、`pageshow` 放鎖都抽成 `_lib/cvsSelection.ts` 的函式另外測；
 * 這裡驗「畫什麼完全由 props 決定」的那一半。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/storefront';
import { ConvenienceStoreField, ConvenienceStoreFieldView, CvsMapForm } from '../ConvenienceStoreField';

(globalThis as unknown as { React: typeof React }).React = React;

type S = components['schemas'];

const SELECTION: S['CvsStoreSelection'] = {
  selectionId: 'ABCDEFGHIJ0123456789',
  storeCode: '991234',
  storeName: '7-ELEVEN 信義門市',
  storeAddress: '台北市信義區松仁路 100 號',
  isOutlying: false,
  expiresAt: '2026-09-15T10:15:00Z',
};

const neverStart = () => new Promise<S['CvsMapSession']>(() => {});

describe('ConvenienceStoreField：三種狀態', () => {
  it('還沒選：一顆「選擇 7-ELEVEN 門市」＋ 說明，沒有門市名稱、沒有錯誤', () => {
    const html = renderToStaticMarkup(
      <ConvenienceStoreField selection={null} loading={false} error={null} onStart={neverStart} />,
    );
    expect(html).toContain('選擇 7-ELEVEN 門市');
    expect(html).toContain('會前往 7-ELEVEN 電子地圖，選好後自動回到這一頁');
    expect(html).not.toContain('重新選擇');
    expect(html).not.toContain('role="alert"');
    expect(html).not.toContain('<form');
  });

  it('已選：名稱、地址、「重新選擇」；不是離島就沒有離島字樣', () => {
    const html = renderToStaticMarkup(
      <ConvenienceStoreField selection={SELECTION} loading={false} error={null} onStart={neverStart} />,
    );
    expect(html).toContain('7-ELEVEN 信義門市');
    expect(html).toContain('台北市信義區松仁路 100 號');
    expect(html).toContain('重新選擇');
    expect(html).not.toContain('離島門市');
    expect(html).not.toContain('會前往 7-ELEVEN 電子地圖');
  });

  it('已選離島門市：加「離島門市」字樣', () => {
    const html = renderToStaticMarkup(
      <ConvenienceStoreField selection={{ ...SELECTION, isOutlying: true }} loading={false} error={null} onStart={neverStart} />,
    );
    expect(html).toContain('離島門市');
  });

  it('錯誤：role="alert" 顯示訊息，按鈕仍可按', () => {
    const html = renderToStaticMarkup(
      <ConvenienceStoreField
        selection={null}
        loading={false}
        error="門市選擇已逾時，請重新選擇門市。"
        onStart={neverStart}
      />,
    );
    expect(html).toMatch(/role="alert"[^>]*>門市選擇已逾時，請重新選擇門市。</);
    expect(html).toContain('選擇 7-ELEVEN 門市');
    // 比對屬性 `disabled=""`，不是 class 名裡的 Tailwind `disabled:` 前綴。
    expect(html).toMatch(/<button[^>]*>/);
    expect(html).not.toMatch(/<button[^>]*\sdisabled=""/);
  });

  it('讀取中：Skeleton，不顯示門市也不顯示說明', () => {
    const html = renderToStaticMarkup(
      <ConvenienceStoreField selection={null} loading error={null} onStart={neverStart} />,
    );
    expect(html).toContain('animate-pulse');
    expect(html).not.toContain('會前往 7-ELEVEN 電子地圖');
  });

  it('拿到地圖參數之後：畫出隱藏表單與「如果沒有自動跳轉，請點這裡」', () => {
    const html = renderToStaticMarkup(
      <ConvenienceStoreFieldView
        selection={null}
        loading={false}
        error={null}
        starting
        session={{
          selectionId: 'ABCDEFGHIJ0123456789',
          method: 'POST',
          action: 'https://logistics-stage.ecpay.com.tw/Express/map',
          fields: { ExtraData: 'ABCDEFGHIJ0123456789' },
          expiresAt: '2026-09-15T10:15:00Z',
        }}
      />,
    );
    expect(html).toContain('<form');
    expect(html).toContain('如果沒有自動跳轉，請點這裡');
  });
});

describe('CvsMapForm：地圖導轉表單', () => {
  const session: S['CvsMapSession'] = {
    selectionId: 'ABCDEFGHIJ0123456789',
    method: 'POST',
    action: 'https://logistics-stage.ecpay.com.tw/Express/map',
    fields: {
      MerchantID: '2000933',
      LogisticsType: 'CVS',
      LogisticsSubType: 'UNIMARTC2C',
      IsCollection: 'N',
      ServerReplyURL: 'https://api.example.tw/v1/logistics/cvs-map/reply',
      ExtraData: 'ABCDEFGHIJ0123456789',
      Weird: 'a"b<c&d',
    },
    expiresAt: '2026-09-15T10:15:00Z',
  };

  const html = renderToStaticMarkup(<CvsMapForm session={session} />);

  it('method 與 action 正確', () => {
    expect(html).toContain('method="POST"');
    expect(html).toContain('action="https://logistics-stage.ecpay.com.tw/Express/map"');
  });

  it('每個 fields 都成為隱藏欄位', () => {
    for (const name of Object.keys(session.fields)) {
      expect(html).toContain(`type="hidden" name="${name}"`);
    }
    expect(html.match(/type="hidden"/g)).toHaveLength(Object.keys(session.fields).length);
  });

  it('值含 " < & 時被正確跳脫', () => {
    expect(html).toContain('name="Weird" value="a&quot;b&lt;c&amp;d"');
    expect(html).not.toContain('a"b<c&d');
  });

  it('欄位順序不變', () => {
    const names = [...html.matchAll(/type="hidden" name="([^"]+)"/g)].map((match) => match[1]);
    expect(names).toEqual(Object.keys(session.fields));
  });
});
