/**
 * Admin 的固定假資料。**FE-6～FE-8 的資料來源**——負債現金比、分錄借貸相等、
 * 混合三種 line 狀態的訂單這幾個驗收項目，答案都在這支檔案裡的 fixture 是否夠真。
 */

import type { components } from '../types.admin';
import { hexId } from './ids';

type S = components['schemas'];

function twd(major: number): S['Money'] {
  return { amountMinor: Math.round(major * 100), currency: 'TWD' };
}

function daysAgoIso(days: number): string {
  return new Date(Date.now() - days * 86_400_000).toISOString();
}

function daysFromNowDate(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
}

function futureTaipeiDeadline(days: number): string {
  const date = new Date(Date.now() + days * 86_400_000);
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Taipei', year: 'numeric', month: '2-digit', day: '2-digit' })
      .formatToParts(date)
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value]),
  );
  return `${parts.year}-${parts.month}-${parts.day}T23:59:59+08:00`;
}

// ── 團隊成員 ──────────────────────────────────────────────────────────────

export const staffFixture: S['Staff'] = {
  id: hexId('staff:陳店長'),
  displayName: '陳店長',
  email: 'owner@greygray.tw',
  role: 'Owner',
};

// ── 分類 ──────────────────────────────────────────────────────────────────

export const CATEGORY_IDS = {
  mask: hexId('admin-category:面膜保養'),
  kbeauty: hexId('admin-category:韓國藥妝'),
  supplement: hexId('admin-category:保健食品'),
} as const;

export const categories: S['Category'][] = [
  { id: CATEGORY_IDS.mask, name: '面膜保養', imageUrl: null, sortOrder: 1, parentId: CATEGORY_IDS.kbeauty },
  { id: CATEGORY_IDS.kbeauty, name: '韓國藥妝', imageUrl: null, sortOrder: 2, parentId: null },
  { id: CATEGORY_IDS.supplement, name: '保健食品', imageUrl: null, sortOrder: 3, parentId: null },
];

// ── 商品與 SKU ────────────────────────────────────────────────────────────

interface AdminProductSeed {
  readonly key: string;
  readonly name: string;
  readonly categoryId: string;
  readonly mode: S['FulfillmentMode'];
  readonly listPriceNTD: number | null;
  readonly weightGram: number;
}

const ADMIN_PRODUCT_SEEDS: readonly AdminProductSeed[] = [
  { key: '森田藥粧-玻尿酸保濕面膜', name: '森田藥粧 玻尿酸保濕面膜', categoryId: CATEGORY_IDS.mask, mode: 'Stock', listPriceNTD: 399, weightGram: 120 },
  { key: '悅詩風吟-綠茶籽保濕精華', name: '悅詩風吟 綠茶籽保濕精華', categoryId: CATEGORY_IDS.kbeauty, mode: 'Preorder', listPriceNTD: null, weightGram: 150 },
  { key: '若元錠EX', name: '若元錠 EX', categoryId: CATEGORY_IDS.supplement, mode: 'Stock', listPriceNTD: 890, weightGram: 400 },
];

function buildAdminProduct(seed: AdminProductSeed): S['AdminProduct'] {
  const skuId = hexId(`admin-sku:${seed.key}`);
  const sku: S['AdminSku'] = {
    id: skuId,
    available: seed.mode === 'Preorder' ? 0 : 60,
    name: seed.name,
    variantName: null,
    weightGram: seed.weightGram,
    size: { lengthCm: 8, widthCm: 6, heightCm: 4 },
    unitOfMeasure: null,
    unitCount: null,
    listPrice: seed.listPriceNTD !== null ? twd(seed.listPriceNTD) : null,
    isActive: true,
  };
  return {
    id: hexId(`admin-product:${seed.key}`),
    skus: [sku],
    name: seed.name,
    description: null,
    shortDescription: null,
    categoryId: seed.categoryId,
    mode: seed.mode,
    images: [],
    isActive: true,
  };
}

export const adminProducts: S['AdminProduct'][] = ADMIN_PRODUCT_SEEDS.map(buildAdminProduct);

export function adminSkuOf(seedKey: string): S['AdminSku'] {
  const found = adminProducts.find((p) => p.id === hexId(`admin-product:${seedKey}`));
  const sku = found?.skus[0];
  if (!sku) throw new Error(`mock fixture 找不到 admin SKU：${seedKey}`);
  return sku;
}

// ── 開團 ──────────────────────────────────────────────────────────────────

export const CAMPAIGN_IDS = {
  seoul: hexId('admin-campaign:首爾美妝採購團'),
  osaka: hexId('admin-campaign:大阪藥妝團-草稿'),
} as const;

const seoulOffer: S['AdminCampaignOffer'] = {
  id: hexId('admin-offer:seoul:悅詩風吟'),
  skuId: adminSkuOf('悅詩風吟-綠茶籽保濕精華').id,
  name: '悅詩風吟 綠茶籽保濕精華',
  variantName: null,
  sellingPrice: twd(780),
  targetPurchasePrice: twd(520),
  isActive: true,
  orderedQuantity: 18,
};

export const adminCampaigns: S['AdminCampaign'][] = [
  {
    id: CAMPAIGN_IDS.seoul,
    status: 'Open',
    orderCount: 12,
    tripCostTotal: null,
    title: '首爾美妝採購團．九月班',
    destination: '首爾',
    departAt: daysFromNowDate(20),
    returnAt: daysFromNowDate(25),
    closesAt: new Date(Date.now() + 72 * 3_600_000).toISOString(),
    description: '首爾當地代購，出發前收單。',
    coverImageUrl: null,
  },
  {
    id: CAMPAIGN_IDS.osaka,
    status: 'Draft',
    orderCount: 0,
    tripCostTotal: null,
    title: '大阪藥妝團．十月班',
    destination: '大阪',
    departAt: daysFromNowDate(35),
    returnAt: daysFromNowDate(40),
    closesAt: new Date(Date.now() + 240 * 3_600_000).toISOString(),
    description: '草稿中，尚未發布。',
    coverImageUrl: null,
  },
];

export const adminCampaignOffersByCampaignId = new Map<string, S['AdminCampaignOffer'][]>([
  [CAMPAIGN_IDS.seoul, [seoulOffer]],
  [CAMPAIGN_IDS.osaka, []],
]);

export function adminCampaignDetailOf(campaignId: string): S['AdminCampaignDetail'] | null {
  const campaign = adminCampaigns.find((c) => c.id === campaignId);
  if (!campaign) return null;
  return { ...campaign, offers: adminCampaignOffersByCampaignId.get(campaignId) ?? [] };
}

// ── 訂單（後台視角）───────────────────────────────────────────────────────

const orderLineMixed: S['AdminOrderLine'][] = [
  {
    id: hexId('admin-orderline:已出貨'),
    skuId: adminSkuOf('森田藥粧-玻尿酸保濕面膜').id,
    name: '森田藥粧 玻尿酸保濕面膜',
    variantName: null,
    mode: 'Stock',
    status: 'Shipped',
    quantity: 2,
    unitPrice: twd(399),
    lineTotal: twd(798),
    campaignId: null,
    consumedLotId: hexId('lot:森田-2026-08'),
  },
  {
    id: hexId('admin-orderline:缺貨退款'),
    skuId: adminSkuOf('若元錠EX').id,
    name: '若元錠 EX',
    variantName: null,
    mode: 'Stock',
    status: 'Unavailable',
    quantity: 1,
    unitPrice: twd(890),
    lineTotal: twd(890),
    refundedAmount: twd(890),
    campaignId: null,
    consumedLotId: null,
  },
  {
    id: hexId('admin-orderline:待採購'),
    skuId: adminSkuOf('悅詩風吟-綠茶籽保濕精華').id,
    name: '悅詩風吟 綠茶籽保濕精華',
    variantName: null,
    mode: 'Preorder',
    status: 'Pending',
    quantity: 1,
    unitPrice: twd(780),
    lineTotal: twd(780),
    campaignId: CAMPAIGN_IDS.seoul,
    consumedLotId: null,
  },
];

const mixedOrderGoodsTotal = orderLineMixed.reduce((sum, l) => sum + l.lineTotal.amountMinor, 0);

const mixedOrder: S['AdminOrder'] = {
  id: hexId('admin-order:混合狀態'),
  orderNumber: 'GG26082800031',
  customerId: hexId('customer:王小美'),
  customerDisplayName: '王小美',
  status: 'Purchasing',
  grandTotal: { amountMinor: mixedOrderGoodsTotal + 6000, currency: 'TWD' },
  placedAt: daysAgoIso(6),
  campaignId: CAMPAIGN_IDS.seoul,
  goodsTotal: { amountMinor: mixedOrderGoodsTotal, currency: 'TWD' },
  shippingFee: twd(60),
  deliveryMethod: 'ConvenienceStore',
  shippingPolicy: 'ShipSeparately',
  convenienceStoreCode: '991234',
  convenienceStoreName: '7-ELEVEN 信義門市',
  convenienceStoreAddress: '台北市信義區松仁路 100 號',
  lines: orderLineMixed,
  payments: [
    {
      id: hexId('payment:混合狀態'),
      provider: 'ECPay',
      status: 'Captured',
      amount: { amountMinor: mixedOrderGoodsTotal + 6000, currency: 'TWD' },
      fee: twd(28),
      providerTransactionId: '2026082800001234',
      capturedAt: daysAgoIso(6),
      settledAt: daysAgoIso(4),
    },
  ],
  quoteExplain: ['超商取貨一口價 NT$60（ADR-010）。'],
  // ADR-039：後台全員看明文，遮罩欄位恆為 null（保留只為相容舊用戶端）。
  recipientName: '王小美',
  recipientPhone: '0912345678',
  // 超商取貨沒有收件地址（門市看 convenienceStore* 三個欄位）。
  recipientAddress: null,
  customerContactMasked: null,
};

const homeDeliveryOrderGoodsTotal = 780;

/** 宅配訂單：後台要看得到 `recipientAddress`，不然出貨的人知道寄給誰、不知道寄到哪。 */
const homeDeliveryOrder: S['AdminOrder'] = {
  id: hexId('admin-order:宅配'),
  orderNumber: 'GG26082800040',
  customerId: hexId('customer:王小美'),
  customerDisplayName: '王小美',
  status: 'ReadyToShip',
  grandTotal: { amountMinor: homeDeliveryOrderGoodsTotal + 6000, currency: 'TWD' },
  placedAt: daysAgoIso(2),
  campaignId: null,
  goodsTotal: { amountMinor: homeDeliveryOrderGoodsTotal, currency: 'TWD' },
  shippingFee: twd(60),
  deliveryMethod: 'HomeDelivery',
  shippingPolicy: 'ShipSeparately',
  lines: [
    {
      id: hexId('admin-orderline:宅配'),
      skuId: adminSkuOf('若元錠EX').id,
      name: adminSkuOf('若元錠EX').name,
      variantName: null,
      mode: 'Stock',
      status: 'Reserved',
      quantity: 1,
      unitPrice: twd(homeDeliveryOrderGoodsTotal),
      lineTotal: twd(homeDeliveryOrderGoodsTotal),
      campaignId: null,
      consumedLotId: null,
    },
  ],
  payments: [],
  quoteExplain: ['宅配到府（ADR-010）。'],
  recipientName: '王小美',
  recipientPhone: '0912345678',
  // ADR-039：宅配收件地址，下單當時凍結的完整單行字串。
  recipientAddress: '110 台北市信義區松仁路 100 號 5 樓',
  customerContactMasked: null,
};

function buildSimpleAdminOrder(seedKey: string, status: S['OrderStatus'], daysAgo: number, index: number): S['AdminOrder'] {
  const sku = adminSkuOf(seedKey);
  const unitPrice = sku.listPrice ?? twd(500);
  const line: S['AdminOrderLine'] = {
    id: hexId(`admin-orderline:${seedKey}:${status}`),
    skuId: sku.id,
    name: sku.name,
    variantName: null,
    mode: 'Stock',
    status: status === 'Cancelled' ? 'Cancelled' : 'Reserved',
    quantity: 1,
    unitPrice,
    lineTotal: unitPrice,
    campaignId: null,
    consumedLotId: null,
  };
  const grandTotal = { amountMinor: unitPrice.amountMinor + 6000, currency: 'TWD' as const };
  return {
    id: hexId(`admin-order:${status}:${index}`),
    orderNumber: `GG2608280${String(20 + index).padStart(2, '0')}`,
    customerId: hexId('customer:王小美'),
    customerDisplayName: '王小美',
    status,
    grandTotal,
    placedAt: daysAgoIso(daysAgo),
    campaignId: null,
    goodsTotal: unitPrice,
    shippingFee: twd(60),
    deliveryMethod: 'ConvenienceStore',
    shippingPolicy: 'ShipSeparately',
    lines: [line],
    payments: [],
    quoteExplain: ['超商取貨一口價 NT$60（ADR-010）。'],
    recipientName: '王小美',
    recipientPhone: '0912345678',
    // 超商取貨沒有收件地址（門市看 convenienceStore* 三個欄位）。
    recipientAddress: null,
    customerContactMasked: null,
  };
}

const atmInstructionsOrderBase = buildSimpleAdminOrder('森田藥粧-玻尿酸保濕面膜', 'AwaitingPayment', 0, 58);
const atmDeadline = futureTaipeiDeadline(3);
const atmInstructionsOrder: S['AdminOrder'] = {
  ...atmInstructionsOrderBase,
  id: hexId('admin-order:FE58:ATM已取號'),
  orderNumber: 'GG26100100058',
  paymentDueAt: atmDeadline,
  payments: [{
    id: hexId('payment:FE58:ATM已取號'),
    provider: 'ECPay',
    status: 'InstructionsIssued',
    method: 'Atm',
    amount: atmInstructionsOrderBase.grandTotal,
    fee: null,
    providerTransactionId: null,
    instructions: {
      method: 'Atm',
      bankCode: '822',
      virtualAccount: '00998877665544',
      expiresAt: atmDeadline,
      issuedAt: daysAgoIso(0),
    },
    manualRefund: null,
    capturedAt: null,
    settledAt: null,
  }],
};

const pendingRefundOrderBase = buildSimpleAdminOrder('若元錠EX', 'Cancelled', 4, 59);
const pendingRefundOrder: S['AdminOrder'] = {
  ...pendingRefundOrderBase,
  id: hexId('admin-order:FE58:超商代碼待人工退款'),
  orderNumber: 'GG26100100059',
  cancellationSource: 'PaymentExpired',
  payments: [{
    id: hexId('payment:FE58:超商代碼待人工退款'),
    provider: 'ECPay',
    status: 'Captured',
    method: 'ConvenienceStoreCode',
    amount: pendingRefundOrderBase.grandTotal,
    fee: twd(30),
    providerTransactionId: 'FE58-CVS-00059',
    instructions: {
      method: 'ConvenienceStoreCode',
      paymentNo: 'CVS590059',
      expiresAt: daysAgoIso(2),
      issuedAt: daysAgoIso(4),
    },
    manualRefund: {
      status: 'Pending',
      requiredAmount: twd(1_000),
      recordedAmount: twd(300),
      outstandingAmount: twd(700),
      entries: [{
        id: hexId('manual-refund-entry:FE58:partial'),
        amount: twd(300),
        remittedOn: daysFromNowDate(-1),
        note: '第一次部分匯款，後五碼 0059',
        recordedBy: staffFixture.id,
        recordedByName: staffFixture.displayName,
        recordedAt: daysAgoIso(1),
      }],
    },
    capturedAt: daysAgoIso(1),
    settledAt: null,
  }],
};

const completedRefundOrderBase = buildSimpleAdminOrder('森田藥粧-玻尿酸保濕面膜', 'Cancelled', 8, 60);
const completedRefundOrder: S['AdminOrder'] = {
  ...completedRefundOrderBase,
  id: hexId('admin-order:FE58:條碼人工退款完成'),
  orderNumber: 'GG26100100060',
  cancellationSource: 'Staff',
  payments: [{
    id: hexId('payment:FE58:條碼人工退款完成'),
    provider: 'ECPay',
    status: 'Captured',
    method: 'Barcode',
    amount: completedRefundOrderBase.grandTotal,
    fee: twd(30),
    providerTransactionId: 'FE58-BARCODE-00060',
    instructions: {
      method: 'Barcode',
      barcodes: ['BARCODE-60-A', 'BARCODE-60-B', 'BARCODE-60-C'],
      expiresAt: daysAgoIso(5),
      issuedAt: daysAgoIso(8),
    },
    manualRefund: {
      status: 'Completed',
      requiredAmount: twd(500),
      recordedAmount: twd(500),
      outstandingAmount: twd(0),
      entries: [{
        id: hexId('manual-refund-entry:FE58:completed'),
        amount: twd(500),
        remittedOn: daysFromNowDate(-2),
        note: null,
        recordedBy: staffFixture.id,
        recordedByName: staffFixture.displayName,
        recordedAt: daysAgoIso(2),
      }],
    },
    capturedAt: daysAgoIso(6),
    settledAt: daysAgoIso(5),
  }],
};

export const adminOrders: S['AdminOrder'][] = [
  mixedOrder,
  homeDeliveryOrder,
  buildSimpleAdminOrder('森田藥粧-玻尿酸保濕面膜', 'AwaitingPayment', 0, 1),
  buildSimpleAdminOrder('若元錠EX', 'Completed', 20, 2),
  buildSimpleAdminOrder('森田藥粧-玻尿酸保濕面膜', 'Cancelled', 5, 3),
  atmInstructionsOrder,
  pendingRefundOrder,
  completedRefundOrder,
];

export function adminOrderListItemOf(order: S['AdminOrder']): S['AdminOrderListItem'] {
  return {
    id: order.id,
    orderNumber: order.orderNumber,
    // fixture 一律有值，這裡的 `!` 只是把「optional 因為契約容許沒有」的型別收窄成「這筆一定有」。
    customerId: order.customerId!,
    customerDisplayName: order.customerDisplayName,
    status: order.status,
    grandTotal: order.grandTotal,
    placedAt: order.placedAt,
    campaignId: order.campaignId ?? null,
  };
}

export const adminOrderListItems: S['AdminOrderListItem'][] = adminOrders.map(adminOrderListItemOf);

// ── 分錄與帳務 ────────────────────────────────────────────────────────────

export const journalEntries: S['JournalEntry'][] = [
  {
    id: hexId('journal:訂單GG26082800031-收款'),
    occurredAt: daysAgoIso(6),
    postedAt: daysAgoIso(6),
    sourceModule: 'Ordering',
    sourceRef: mixedOrder.orderNumber,
    memo: '訂單付款',
    lines: [
      { accountCode: '1120', accountName: '應收帳款－金流商', direction: 'Debit', amount: mixedOrder.grandTotal },
      { accountCode: '2110', accountName: '預收貨款', direction: 'Credit', amount: mixedOrder.goodsTotal },
      { accountCode: '2120', accountName: '預收運費', direction: 'Credit', amount: mixedOrder.shippingFee },
    ],
  },
  {
    id: hexId('journal:訂單GG26082800031-缺貨退款'),
    occurredAt: daysAgoIso(2),
    postedAt: daysAgoIso(2),
    sourceModule: 'Ordering',
    sourceRef: mixedOrder.orderNumber,
    memo: '缺貨退款（退成儲值金）',
    lines: [
      { accountCode: '2110', accountName: '預收貨款', direction: 'Debit', amount: twd(890) },
      { accountCode: '2210', accountName: '客戶儲值金', direction: 'Credit', amount: twd(890) },
    ],
  },
];

export const campaignMarginFixture: S['CampaignMargin'] = {
  campaignId: CAMPAIGN_IDS.seoul,
  salesRevenue: twd(140_400),
  costOfGoodsSold: twd(93_600),
  shippingRevenue: twd(720),
  shippingCost: twd(600),
  tripCost: twd(18_000),
  grossMargin: twd(28_920),
};

/** `isBreached = true`：正在用還沒交貨的錢過日子，後台首頁要顯眼提示。 */
export const liabilityVsCashFixture: S['LiabilityVsCash'] = {
  customerLiabilityTotal: twd(2_480_000),
  cashTotal: twd(1_930_000),
  isBreached: true,
  asOf: new Date().toISOString(),
};
