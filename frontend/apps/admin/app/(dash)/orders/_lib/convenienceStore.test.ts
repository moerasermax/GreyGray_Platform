import { describe, expect, it } from 'vitest';
import { UNRECORDED_STORE_TEXT, convenienceStoreDisplay } from './convenienceStore';

/**
 * 三個門市欄位在生成型別裡是「可省略又可為 null」。
 * 每一種組合都要畫得出人看得懂的字，而且 `undefined` 不能漏進畫面。
 */
describe('convenienceStoreDisplay：後台「取貨門市」那一格', () => {
  it('名稱＋代號＋地址 → 名稱（代號）、下一行地址', () => {
    expect(
      convenienceStoreDisplay({
        convenienceStoreCode: '991234',
        convenienceStoreName: '7-ELEVEN 信義門市',
        convenienceStoreAddress: '台北市信義區松仁路 100 號',
      }),
    ).toEqual({ primary: '7-ELEVEN 信義門市（991234）', address: '台北市信義區松仁路 100 號' });
  });

  it('只有代號（ADR-038 之前的舊訂單）→ 只顯示代號，沒有括號', () => {
    expect(convenienceStoreDisplay({ convenienceStoreCode: '991234', convenienceStoreName: null, convenienceStoreAddress: null })).toEqual({
      primary: '991234',
      address: null,
    });
  });

  it('只有名稱 → 只顯示名稱，不畫空括號', () => {
    const display = convenienceStoreDisplay({ convenienceStoreName: '7-ELEVEN 信義門市' });
    expect(display).toEqual({ primary: '7-ELEVEN 信義門市', address: null });
    expect(display.primary).not.toContain('（');
  });

  it('名稱＋地址、沒有代號 → 名稱與地址，沒有括號', () => {
    expect(
      convenienceStoreDisplay({
        convenienceStoreCode: null,
        convenienceStoreName: '7-ELEVEN 信義門市',
        convenienceStoreAddress: '台北市信義區松仁路 100 號',
      }),
    ).toEqual({ primary: '7-ELEVEN 信義門市', address: '台北市信義區松仁路 100 號' });
  });

  it('只有地址 → 地址照樣顯示', () => {
    expect(convenienceStoreDisplay({ convenienceStoreAddress: '台北市信義區松仁路 100 號' })).toEqual({
      primary: null,
      address: '台北市信義區松仁路 100 號',
    });
  });

  it('三個都是 null → 未記錄門市', () => {
    expect(
      convenienceStoreDisplay({ convenienceStoreCode: null, convenienceStoreName: null, convenienceStoreAddress: null }),
    ).toEqual({ primary: UNRECORDED_STORE_TEXT, address: null });
  });

  it('三個都省略（undefined）→ 一樣是未記錄門市', () => {
    expect(convenienceStoreDisplay({})).toEqual({ primary: UNRECORDED_STORE_TEXT, address: null });
  });

  it('undefined 與 null 混用：代號 undefined、名稱有值 → 不會出現「（undefined）」', () => {
    const display = convenienceStoreDisplay({ convenienceStoreCode: undefined, convenienceStoreName: '信義門市' });
    expect(display.primary).toBe('信義門市');
    expect(JSON.stringify(display)).not.toContain('undefined');
  });

  it('名稱 undefined、代號有值 → 只顯示代號', () => {
    expect(convenienceStoreDisplay({ convenienceStoreCode: '991234', convenienceStoreName: undefined }).primary).toBe('991234');
  });

  it('空字串當作沒有', () => {
    expect(convenienceStoreDisplay({ convenienceStoreCode: '', convenienceStoreName: '  ', convenienceStoreAddress: '' })).toEqual({
      primary: UNRECORDED_STORE_TEXT,
      address: null,
    });
  });
});
