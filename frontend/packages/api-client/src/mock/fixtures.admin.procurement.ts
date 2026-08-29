/**
 * 現場採購清單（M1b-1）的 mock 假資料。獨立於 `fixtures.admin.ts`（FE-9 的檔案），
 * 只讀它匯出的 `CAMPAIGN_IDS`／`adminSkuOf`，不改它一個字。
 *
 * `CAMPAIGN_IDS.seoul` 在 `fixtures.admin.ts` 裡目前是 `Open` 狀態——
 * 那份 fixture 沒有任何 `Closed` 階段的團（截團後才會產生採購清單，見 M1b-1 說明）。
 * 這裡照樣把採購清單掛在 `seoul` 底下，讓這一包能夠自驗；
 * 缺一個「已截團」狀態的團 fixture 是已知落差，回報給整合者，不在這一包補（那是 FE-9 的檔案）。
 */
import type { components } from '../types.admin';
import { hexId } from './ids';
import { adminSkuOf, CAMPAIGN_IDS } from './fixtures.admin';

type S = components['schemas'];

function twd(major: number): S['Money'] {
  return { amountMinor: Math.round(major * 100), currency: 'TWD' };
}

function jpy(major: number): S['Money'] {
  return { amountMinor: Math.round(major), currency: 'JPY' };
}

export const PURCHASE_ITEM_IDS = {
  pendingMask: hexId('purchase-item:森田藥粧-待買'),
  purchasedSerum: hexId('purchase-item:悅詩風吟-已買'),
  unavailableSupplement: hexId('purchase-item:若元錠-缺貨'),
} as const;

const seoulPurchaseItems: S['PurchaseItem'][] = [
  {
    id: PURCHASE_ITEM_IDS.pendingMask,
    skuId: adminSkuOf('森田藥粧-玻尿酸保濕面膜').id,
    name: '森田藥粧 玻尿酸保濕面膜',
    variantName: null,
    imageUrl: null,
    orderLineId: hexId('admin-orderline:procurement:mask'),
    orderNumber: 'GG26082800031',
    quantityRequested: 2,
    quantityPurchased: 0,
    targetPrice: jpy(1200),
    status: 'Pending',
    decidedAt: null,
    inquiry: null,
  },
  {
    id: PURCHASE_ITEM_IDS.purchasedSerum,
    skuId: adminSkuOf('悅詩風吟-綠茶籽保濕精華').id,
    name: '悅詩風吟 綠茶籽保濕精華',
    variantName: null,
    imageUrl: null,
    orderLineId: hexId('admin-orderline:procurement:serum'),
    orderNumber: 'GG26082800032',
    quantityRequested: 1,
    quantityPurchased: 1,
    targetPrice: twd(520),
    status: 'Purchased',
    decidedAt: new Date(Date.now() - 3_600_000).toISOString(),
    inquiry: null,
  },
  {
    id: PURCHASE_ITEM_IDS.unavailableSupplement,
    skuId: adminSkuOf('若元錠EX').id,
    name: '若元錠 EX',
    variantName: null,
    imageUrl: null,
    orderLineId: hexId('admin-orderline:procurement:supplement'),
    orderNumber: 'GG26082800033',
    quantityRequested: 1,
    quantityPurchased: 0,
    targetPrice: null,
    status: 'Unavailable',
    decidedAt: new Date(Date.now() - 7_200_000).toISOString(),
    inquiry: null,
  },
];

/** campaignId → 該團的採購清單。目前只有 `CAMPAIGN_IDS.seoul` 有資料，見檔頭說明。 */
export const purchaseItemsByCampaignId = new Map<string, S['PurchaseItem'][]>([
  [CAMPAIGN_IDS.seoul, seoulPurchaseItems],
]);
