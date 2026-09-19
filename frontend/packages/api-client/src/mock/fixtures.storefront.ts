/**
 * Storefront 的固定假資料。**這是 FE-3～FE-5 全部人的資料來源**，
 * 所以品名、金額、截團時間都刻意做得「像真的」——中文藥妝品名、真實台幣金額、
 * 現貨與預購混合的購物車，用「商品1 商品2」做不出真正的版面問題。
 *
 * 這裡的函式回傳的都是**這次呼叫當下的快照**，不是共用可變物件——
 * handlers 裡才決定哪些要用可變的 store（購物車），哪些每次都重新算（`now` 相依的截團倒數）。
 */

import type { components } from '../types.storefront';
import { hexId } from './ids';

type S = components['schemas'];

/** 用台幣「元」寫 fixture 比較好讀，內部照契約換成 `amountMinor`（分）。 */
function twd(major: number): S['Money'] {
  return { amountMinor: Math.round(major * 100), currency: 'TWD' };
}

function hoursFromNow(hours: number): string {
  return new Date(Date.now() + hours * 3_600_000).toISOString();
}

function daysFromNow(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
}

// ── 分類 ──────────────────────────────────────────────────────────────────

export const CATEGORY_IDS = {
  mask: hexId('category:面膜保養'),
  makeup: hexId('category:彩妝'),
  supplement: hexId('category:保健食品'),
  baby: hexId('category:母嬰用品'),
  household: hexId('category:生活雜貨'),
  kbeauty: hexId('category:韓國藥妝'),
} as const;

export const categories: S['Category'][] = [
  { id: CATEGORY_IDS.mask, name: '面膜保養', imageUrl: null },
  { id: CATEGORY_IDS.makeup, name: '彩妝', imageUrl: null },
  { id: CATEGORY_IDS.supplement, name: '保健食品', imageUrl: null },
  { id: CATEGORY_IDS.baby, name: '母嬰用品', imageUrl: null },
  { id: CATEGORY_IDS.household, name: '生活雜貨', imageUrl: null },
  { id: CATEGORY_IDS.kbeauty, name: '韓國藥妝', imageUrl: null },
];

// ── 開團 ──────────────────────────────────────────────────────────────────

export const CAMPAIGN_IDS = {
  seoul: hexId('campaign:首爾美妝採購團'),
  osaka: hexId('campaign:大阪藥妝團'),
  tokyoClosed: hexId('campaign:東京黑五團-已截團'),
  fukuokaCancelled: hexId('campaign:福岡團-取消'),
} as const;

const seoulCampaign: S['CampaignListItem'] = {
  id: CAMPAIGN_IDS.seoul,
  title: '首爾美妝採購團．九月班',
  destination: '首爾',
  departAt: daysFromNow(20),
  returnAt: daysFromNow(25),
  closesAt: hoursFromNow(72),
  status: 'Open',
  isAcceptingOrders: true,
  coverImageUrl: null,
};

const osakaCampaign: S['CampaignListItem'] = {
  id: CAMPAIGN_IDS.osaka,
  title: '大阪藥妝團．十月班',
  destination: '大阪',
  departAt: daysFromNow(35),
  returnAt: daysFromNow(40),
  closesAt: hoursFromNow(240),
  status: 'Open',
  isAcceptingOrders: true,
  coverImageUrl: null,
};

const tokyoClosedCampaign: S['CampaignListItem'] = {
  id: CAMPAIGN_IDS.tokyoClosed,
  title: '東京藥妝黑五團',
  destination: '東京',
  departAt: daysFromNow(-5),
  returnAt: daysFromNow(0),
  closesAt: hoursFromNow(-48),
  status: 'Closed',
  isAcceptingOrders: false,
  coverImageUrl: null,
};

const fukuokaCancelledCampaign: S['CampaignListItem'] = {
  id: CAMPAIGN_IDS.fukuokaCancelled,
  title: '福岡溫泉藥妝團',
  destination: '福岡',
  departAt: daysFromNow(-30),
  returnAt: daysFromNow(-25),
  closesAt: hoursFromNow(-500),
  status: 'Cancelled',
  isAcceptingOrders: false,
  coverImageUrl: null,
};

export const campaignListItems: S['CampaignListItem'][] = [
  seoulCampaign,
  osakaCampaign,
  tokyoClosedCampaign,
  fukuokaCancelledCampaign,
];

// ── 商品 ──────────────────────────────────────────────────────────────────

interface ProductSeed {
  readonly key: string;
  readonly name: string;
  readonly shortDescription: string;
  readonly description: string;
  readonly categoryId: string;
  readonly mode: S['FulfillmentMode'];
  readonly campaignId?: string;
  readonly unitPriceNTD: number | null;
  readonly unitOfMeasure?: string;
  readonly unitCount?: number;
  readonly badges: string[];
  readonly available: number;
  readonly variants?: readonly string[];
  readonly isFavorited?: boolean;
}

const PRODUCT_SEEDS: readonly ProductSeed[] = [
  {
    key: '森田藥粧-玻尿酸保濕面膜',
    name: '森田藥粧 玻尿酸保濕面膜',
    shortDescription: '大容量保濕面膜，敏感肌也能天天敷。',
    description: '添加玻尿酸與神經醯胺，維持肌膚水潤，無酒精無香料配方。',
    categoryId: CATEGORY_IDS.mask,
    mode: 'Stock',
    unitPriceNTD: 399,
    unitOfMeasure: '片',
    unitCount: 10,
    badges: ['Popular'],
    available: 86,
    variants: ['原味保濕', '玻尿酸加強'],
  },
  {
    key: 'DrJart-微生態舒緩面霜',
    name: 'Dr.Jart+ 微生態舒緩面霜',
    shortDescription: '舒緩泛紅，冬天乾癢也能用。',
    description: '微生態科技配方，強化肌膚屏障，質地清爽好推。',
    categoryId: CATEGORY_IDS.mask,
    mode: 'Stock',
    unitPriceNTD: 1280,
    unitOfMeasure: 'ml',
    unitCount: 50,
    badges: ['New'],
    available: 24,
  },
  {
    key: '若元錠EX',
    name: '若元錠 EX',
    shortDescription: '13 種維生素一次補足，熬夜救星。',
    description: '日本熱銷綜合維生素錠，288 錠大容量裝，一天 6 錠。',
    categoryId: CATEGORY_IDS.supplement,
    mode: 'Stock',
    unitPriceNTD: 890,
    unitOfMeasure: '錠',
    unitCount: 288,
    badges: [],
    available: 40,
  },
  {
    key: '大正製藥-表飛鳴S微粒',
    name: '大正製藥 表飛鳴S微粒',
    shortDescription: '腸胃保健必備，全家人都能吃。',
    description: '調整腸道菌叢，改善軟便、便秘與腹脹。',
    categoryId: CATEGORY_IDS.supplement,
    mode: 'Stock',
    unitPriceNTD: 560,
    unitOfMeasure: 'g',
    unitCount: 100,
    badges: [],
    available: 55,
  },
  {
    key: '曼秀雷敦-薄荷腦軟膏',
    name: '曼秀雷敦 薄荷腦軟膏',
    shortDescription: '涼感舒緩，蚊蟲叮咬、痠痛都能擦。',
    description: '經典小護士軟膏，攜帶方便，居家旅行必備。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 150,
    unitOfMeasure: 'g',
    unitCount: 22,
    badges: [],
    available: 120,
  },
  {
    key: '花王-蒸氣熱敷眼罩',
    name: '花王 蒸氣熱敷眼罩',
    shortDescription: '溫感 10 分鐘，加班救星。',
    description: '無香料款，發熱均勻，睡前敷一片放鬆眼周。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 320,
    unitOfMeasure: '片',
    unitCount: 12,
    badges: ['LastCall'],
    available: 6,
  },
  {
    key: '白兔牌-馬油潤唇膏',
    name: '白兔牌 馬油潤唇膏',
    shortDescription: '經典萬用膏，護唇也能護手肘。',
    description: '天然馬油成分，滋潤不黏膩，隨身攜帶款。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 99,
    unitOfMeasure: 'g',
    unitCount: 4,
    badges: [],
    available: 200,
  },
  {
    key: 'LG-竹鹽牙膏',
    name: 'LG 竹鹽牙膏',
    shortDescription: '韓國國民牙膏，清新一整天。',
    description: '竹鹽配方，強化牙齦保健，去除口臭。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 180,
    unitOfMeasure: 'g',
    unitCount: 120,
    badges: [],
    available: 90,
  },
  {
    key: '悅詩風吟-綠茶籽保濕精華',
    name: '悅詩風吟 綠茶籽保濕精華',
    shortDescription: '油痘肌也能安心用的保濕精華。',
    description: '濟州島綠茶萃取，調節油水平衡，清爽不黏膩。',
    categoryId: CATEGORY_IDS.kbeauty,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.seoul,
    unitPriceNTD: 780,
    unitOfMeasure: 'ml',
    unitCount: 80,
    badges: ['Popular'],
    available: 0,
  },
  {
    key: 'innisfree-火山泥毛孔緊緻面膜',
    name: 'innisfree 火山泥毛孔緊緻面膜',
    shortDescription: '濟州火山泥，粗大毛孔的救星。',
    description: '吸附多餘皮脂，洗淨後毛孔明顯緊緻。',
    categoryId: CATEGORY_IDS.kbeauty,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.seoul,
    unitPriceNTD: 450,
    unitOfMeasure: 'ml',
    unitCount: 100,
    badges: ['New'],
    available: 0,
  },
  {
    key: '3CE-絲絨唇釉',
    name: '3CE 絲絨唇釉',
    shortDescription: '一擦顯色，韓妞必備顯白色號。',
    description: '絲絨霧面質地，持久不脫色，多色可選。',
    categoryId: CATEGORY_IDS.kbeauty,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.seoul,
    unitPriceNTD: 620,
    unitOfMeasure: '支',
    unitCount: 1,
    badges: [],
    available: 0,
    variants: ['#TAUPE 木質棕', '#DUSTY 豆沙粉'],
  },
  {
    key: 'CLIO-持久眼線膠筆',
    name: 'CLIO 持久眼線膠筆',
    shortDescription: '24 小時抗暈染，游泳都不怕。',
    description: '極細筆頭好上手，防水抗暈染配方。',
    categoryId: CATEGORY_IDS.kbeauty,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.seoul,
    unitPriceNTD: 480,
    badges: [],
    available: 0,
  },
  {
    key: '正官庄-紅蔘精',
    name: '韓國人蔘公社 正官庄紅蔘精',
    shortDescription: '送禮自用兩相宜的六年根紅蔘。',
    description: '每包濃縮紅蔘精華，方便攜帶即開即飲。',
    categoryId: CATEGORY_IDS.supplement,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.seoul,
    unitPriceNTD: 1680,
    unitOfMeasure: '包',
    unitCount: 30,
    badges: ['Popular'],
    available: 0,
  },
  {
    key: '貝親-防脹氣奶瓶',
    name: '貝親 防脹氣奶瓶',
    shortDescription: '日本人氣奶瓶，減少寶寶脹氣不適。',
    description: '寬口好清洗，特殊奶嘴設計降低吸入空氣。',
    categoryId: CATEGORY_IDS.baby,
    mode: 'Stock',
    unitPriceNTD: 450,
    unitOfMeasure: 'ml',
    unitCount: 240,
    badges: [],
    available: 30,
  },
  {
    key: '好孩子-嬰兒濕紙巾',
    name: '好孩子 嬰兒濕紙巾',
    shortDescription: '純水配方，新生兒也能安心用。',
    description: '99% 純水成分，無酒精無香料，三包量販組。',
    categoryId: CATEGORY_IDS.baby,
    mode: 'Stock',
    unitPriceNTD: 299,
    unitOfMeasure: '抽×3包',
    unitCount: 80,
    badges: [],
    available: 150,
  },
  {
    key: '妙而舒-抗菌洗手慕斯',
    name: '妙而舒 抗菌洗手慕斯',
    shortDescription: '綿密泡沫，小朋友也願意主動洗手。',
    description: '弱酸性配方，溫和不刺激，抗菌不緊繃。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 189,
    unitOfMeasure: 'ml',
    unitCount: 250,
    badges: [],
    available: 70,
  },
  {
    key: '象印-保溫杯',
    name: '象印 保溫杯',
    shortDescription: '6 小時長效保溫，通勤上班族首選。',
    description: '不鏽鋼真空斷熱構造，輕巧好攜帶。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 1180,
    unitOfMeasure: 'ml',
    unitCount: 480,
    badges: [],
    available: 18,
  },
  {
    key: '小林製藥-暖暖包',
    name: '小林製藥 暖暖包',
    shortDescription: '冬天必備，出遊上班都能用。',
    description: '12 小時持續發熱，可貼式不燙手。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: 220,
    unitOfMeasure: '入',
    unitCount: 30,
    badges: ['LastCall'],
    available: 3,
  },
  {
    key: 'Anessa-完全防曬乳',
    name: 'Anessa 完全防曬乳 金鑽版',
    shortDescription: '日本防曬銷售冠軍，抗汗抗水。',
    description: 'SPF50+ PA++++，遇水遇汗防曬力更強。',
    categoryId: CATEGORY_IDS.mask,
    mode: 'Stock',
    unitPriceNTD: 980,
    unitOfMeasure: 'ml',
    unitCount: 60,
    badges: ['Popular'],
    available: 45,
  },
  {
    key: '雪花秀-潤燥精華',
    name: '雪花秀 潤燥精華',
    shortDescription: '韓方保養代表作，秋冬乾燥救星。',
    description: '韓方草本配方，深層滋潤不黏膩。',
    categoryId: CATEGORY_IDS.kbeauty,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.osaka,
    unitPriceNTD: 2280,
    unitOfMeasure: 'ml',
    unitCount: 60,
    badges: [],
    available: 0,
  },
  {
    key: 'Whoo后-拱辰享美白精華',
    name: 'Whoo 后 拱辰享美白精華',
    shortDescription: '宮廷御用配方，送禮自用都體面。',
    description: '韓方美白精華，均勻膚色，提升肌膚光澤。',
    categoryId: CATEGORY_IDS.kbeauty,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.osaka,
    unitPriceNTD: 3280,
    unitOfMeasure: 'ml',
    unitCount: 45,
    badges: ['New'],
    available: 0,
  },
  {
    key: '綠十字-蒎樂欣感冒糖漿',
    name: '綠十字 蒎樂欣感冒糖漿',
    shortDescription: '韓國家庭常備感冒糖漿。',
    description: '舒緩喉嚨痛與咳嗽症狀，草莓口味好入口。',
    categoryId: CATEGORY_IDS.supplement,
    mode: 'Stock',
    unitPriceNTD: 260,
    unitOfMeasure: 'ml',
    unitCount: 120,
    badges: [],
    available: 65,
  },
  {
    key: '資生堂-安耐曬金鑽版',
    name: '資生堂 安耐曬 金鑽版',
    shortDescription: '東京黑五團限定帶回，數量有限。',
    description: 'SPF50+ PA++++，金鑽版包裝限定款。',
    categoryId: CATEGORY_IDS.mask,
    mode: 'Preorder',
    campaignId: CAMPAIGN_IDS.tokyoClosed,
    unitPriceNTD: 1080,
    unitOfMeasure: 'ml',
    unitCount: 60,
    badges: ['LastCall'],
    available: 0,
  },
  {
    key: '蜂膠喉糖',
    name: '南光 蜂膠喉糖',
    shortDescription: '換季必備，辦公室桌上常備款。',
    description: '天然蜂膠萃取，舒緩喉嚨不適，薄荷涼感。',
    categoryId: CATEGORY_IDS.supplement,
    mode: 'Stock',
    unitPriceNTD: 120,
    unitOfMeasure: '錠',
    unitCount: 40,
    badges: [],
    available: 300,
    isFavorited: true,
  },
  {
    key: '旅行收納袋-未定價',
    name: '旅行收納袋',
    shortDescription: '新品價格確認中，先收藏之後再回來看。',
    description: '輕量旅行收納袋，商品已上架，售價仍在確認中。',
    categoryId: CATEGORY_IDS.household,
    mode: 'Stock',
    unitPriceNTD: null,
    badges: ['New'],
    available: 20,
    isFavorited: true,
  },
];

interface BuiltProduct {
  readonly listItem: S['ProductListItem'];
  readonly detail: S['ProductDetail'];
  readonly skuIds: string[];
}

function buildProduct(seed: ProductSeed): BuiltProduct {
  const productId = hexId(`product:${seed.key}`);
  const price = seed.unitPriceNTD === null ? null : twd(seed.unitPriceNTD);
  const variantNames = seed.variants ?? [seed.name];
  const campaignOfferIdFor = (variantName: string): string | null =>
    seed.mode === 'Preorder' ? hexId(`offer:${seed.key}:${variantName}`) : null;

  const skus: S['Sku'][] = variantNames.map((variantName, index) => ({
    id: hexId(`sku:${seed.key}:${variantName}`),
    name: seed.name,
    variantName: seed.variants ? variantName : null,
    weightGram: 80 + index * 20,
    size: { lengthCm: 8, widthCm: 6, heightCm: 4 },
    unitOfMeasure: seed.unitOfMeasure ?? null,
    unitCount: seed.unitCount ?? null,
    isActive: true,
    available: seed.mode === 'Preorder' ? 0 : seed.available,
    price,
    campaignOfferId: campaignOfferIdFor(variantName),
  }));

  const unitPriceLabel = seed.unitPriceNTD !== null && seed.unitOfMeasure
    ? `NT$${seed.unitPriceNTD}／${seed.unitCount ?? 1} ${seed.unitOfMeasure}`
    : null;

  const listItem: S['ProductListItem'] = {
    id: productId,
    name: seed.name,
    shortDescription: seed.shortDescription,
    imageUrl: null,
    priceFrom: price,
    unitPriceLabel,
    badges: seed.badges,
    isFavorited: seed.isFavorited ?? false,
    mode: seed.mode,
    campaignId: seed.campaignId ?? null,
  };

  const campaign = seed.campaignId
    ? (campaignListItems.find((c) => c.id === seed.campaignId) ?? null)
    : null;

  const detail: S['ProductDetail'] = {
    id: productId,
    name: seed.name,
    description: seed.description,
    shortDescription: seed.shortDescription,
    images: [],
    categoryId: seed.categoryId,
    mode: seed.mode,
    campaign,
    skus,
    isFavorited: seed.isFavorited ?? false,
  };

  return { listItem, detail, skuIds: skus.map((s) => s.id) };
}

const builtProducts = PRODUCT_SEEDS.map(buildProduct);

export const productListItems: S['ProductListItem'][] = builtProducts.map((p) => p.listItem);

export const productDetailsById = new Map<string, S['ProductDetail']>(
  builtProducts.map((p) => [p.detail.id, p.detail]),
);

/** 給購物車／訂單 fixture 引用：找某個 seed key 對應的第一顆 SKU。 */
export function skuOf(seedKey: string): S['Sku'] {
  const built = builtProducts.find((p) => p.detail.id === hexId(`product:${seedKey}`));
  if (!built) throw new Error(`mock fixture 找不到商品：${seedKey}`);
  const sku = built.detail.skus[0];
  if (!sku) throw new Error(`mock fixture 商品沒有 SKU：${seedKey}`);
  return sku;
}

export function productOf(seedKey: string): S['ProductDetail'] {
  const found = productDetailsById.get(hexId(`product:${seedKey}`));
  if (!found) throw new Error(`mock fixture 找不到商品：${seedKey}`);
  return found;
}

interface SkuLookup {
  readonly sku: S['Sku'];
  readonly product: S['ProductDetail'];
}

const skuLookupById = new Map<string, SkuLookup>(
  builtProducts.flatMap((p) => p.detail.skus.map((sku): [string, SkuLookup] => [sku.id, { sku, product: p.detail }])),
);

/** 加入購物車時用 `skuId` 反查商品——找不到就是 `catalog.sku-not-found`。 */
export function findSkuById(skuId: string): SkuLookup | undefined {
  return skuLookupById.get(skuId);
}

// ── 開團詳情（含開團商品）───────────────────────────────────────────────

function offersOfCampaign(campaignId: string): S['CampaignOffer'][] {
  return builtProducts
    .filter((p) => p.detail.mode === 'Preorder' && p.detail.campaign?.id === campaignId)
    .flatMap((p) =>
      p.detail.skus.map(
        (sku): S['CampaignOffer'] => ({
          id: sku.campaignOfferId ?? hexId(`offer:${p.detail.id}:${sku.id}`),
          skuId: sku.id,
          productId: p.detail.id,
          name: p.detail.name,
          variantName: sku.variantName ?? null,
          imageUrl: null,
          sellingPrice: sku.price ?? twd(0),
          unitPriceLabel: p.listItem.unitPriceLabel ?? null,
          isActive: sku.isActive,
        }),
      ),
    );
}

export const campaignDetailsById = new Map<string, S['CampaignDetail']>(
  campaignListItems.map((campaign) => [
    campaign.id,
    { ...campaign, description: `${campaign.destination}當地代購，出發前 ${campaign.title}。`, offers: offersOfCampaign(campaign.id) },
  ]),
);

// ── 會員 ──────────────────────────────────────────────────────────────────

export const ME_ID = hexId('customer:王小美');

export const meFixture: S['Me'] = {
  id: ME_ID,
  displayName: '王小美',
  tier: 'Standard',
  isActive: true,
  email: 'xiaomei@example.com',
  phoneNumberMasked: '0912***678',
  lineLinked: false,
};

export const ADDRESS_IDS = {
  home: hexId('address:家裡'),
  office: hexId('address:公司'),
} as const;

export const addresses: S['ShippingAddress'][] = [
  {
    id: ADDRESS_IDS.home,
    recipientName: '王小美',
    phoneNumber: '0912345678',
    postalCode: '110',
    city: '台北市',
    district: '信義區',
    streetAddress: '松仁路 100 號 5 樓',
    isDefault: true,
  },
  {
    id: ADDRESS_IDS.office,
    recipientName: '王小美',
    phoneNumber: '0912345678',
    postalCode: '106',
    city: '台北市',
    district: '大安區',
    streetAddress: '復興南路一段 200 號 8 樓之 3',
    isDefault: false,
  },
];

export const storedValueBalance: { balance: S['Money'] } = { balance: twd(350) };

// ── 購物車 ────────────────────────────────────────────────────────────────

function cartLineFrom(seedKey: string, quantity: number, availabilityWarning: string | null = null): S['CartLine'] {
  const sku = skuOf(seedKey);
  const product = productOf(seedKey);
  const unitPrice = sku.price ?? twd(0);
  return {
    id: hexId(`cartline:${seedKey}`),
    skuId: sku.id,
    productId: product.id,
    name: product.name,
    variantName: sku.variantName ?? null,
    imageUrl: null,
    mode: product.mode,
    campaignId: product.campaign?.id ?? null,
    campaignOfferId: sku.campaignOfferId ?? null,
    quantity,
    unitPrice,
    lineTotal: { amountMinor: unitPrice.amountMinor * quantity, currency: unitPrice.currency },
    availabilityWarning,
  };
}

/** 初始購物車：一件現貨 ＋ 一件預購，示範 `hasMixedModes`。 */
export function initialCartLines(): S['CartLine'][] {
  return [cartLineFrom('森田藥粧-玻尿酸保濕面膜', 2), cartLineFrom('悅詩風吟-綠茶籽保濕精華', 1)];
}

export function buildCart(id: string, lines: S['CartLine'][], quote: S['QuoteResult'] | null): S['Cart'] {
  const goodsTotal = lines.reduce((sum, line) => sum + line.lineTotal.amountMinor, 0);
  return {
    id,
    lines,
    goodsTotal: { amountMinor: goodsTotal, currency: 'TWD' },
    hasMixedModes: new Set(lines.map((l) => l.mode)).size > 1,
    quote,
  };
}

export const CART_ID = hexId('cart:anonymous-session');

// ── 訂單 ──────────────────────────────────────────────────────────────────

const SHIPPING_FEE_CONVENIENCE = twd(60);

interface OrderSeed {
  readonly status: S['OrderStatus'];
  readonly daysAgo: number;
  readonly lineStatuses: readonly S['OrderLineStatus'][];
}

const ORDER_SEEDS: readonly OrderSeed[] = [
  { status: 'AwaitingPayment', daysAgo: 0, lineStatuses: ['Pending', 'Pending'] },
  { status: 'PaidAwaitingClose', daysAgo: 1, lineStatuses: ['Reserved', 'Reserved'] },
  { status: 'ClosedAwaitingDeparture', daysAgo: 3, lineStatuses: ['Reserved', 'Reserved'] },
  { status: 'Purchasing', daysAgo: 10, lineStatuses: ['Purchased', 'Unavailable'] },
  { status: 'GoodsReceived', daysAgo: 15, lineStatuses: ['Purchased', 'Purchased'] },
  { status: 'ReadyToShip', daysAgo: 16, lineStatuses: ['Purchased', 'Purchased'] },
  { status: 'Shipped', daysAgo: 18, lineStatuses: ['Shipped', 'Shipped'] },
  { status: 'Completed', daysAgo: 30, lineStatuses: ['Completed', 'Completed'] },
  { status: 'Cancelled', daysAgo: 2, lineStatuses: ['Cancelled', 'Cancelled'] },
];

function buildOrderLine(seedKey: string, quantity: number, status: S['OrderLineStatus']): S['OrderLine'] {
  const sku = skuOf(seedKey);
  const product = productOf(seedKey);
  const unitPrice = sku.price ?? twd(0);
  const isUnavailable = status === 'Unavailable';
  return {
    id: hexId(`orderline:${seedKey}:${status}`),
    skuId: sku.id,
    productId: product.id,
    name: product.name,
    variantName: sku.variantName ?? null,
    imageUrl: null,
    mode: product.mode,
    status,
    quantity,
    unitPrice,
    lineTotal: { amountMinor: unitPrice.amountMinor * quantity, currency: unitPrice.currency },
    campaignId: product.campaign?.id ?? null,
    refundedAmount: isUnavailable ? { amountMinor: unitPrice.amountMinor * quantity, currency: unitPrice.currency } : null,
  };
}

function buildOrder(seed: OrderSeed, index: number): S['Order'] {
  const placedAt = new Date(Date.now() - seed.daysAgo * 86_400_000).toISOString();
  const [stockLineStatus, preorderLineStatus] = seed.lineStatuses;
  const lines = [
    buildOrderLine('森田藥粧-玻尿酸保濕面膜', 2, stockLineStatus ?? 'Pending'),
    buildOrderLine('悅詩風吟-綠茶籽保濕精華', 1, preorderLineStatus ?? 'Pending'),
  ];
  const goodsTotalMinor = lines.reduce((sum, line) => sum + line.lineTotal.amountMinor, 0);
  const grandTotalMinor = goodsTotalMinor + SHIPPING_FEE_CONVENIENCE.amountMinor;
  const isPaid = seed.status !== 'AwaitingPayment' && seed.status !== 'Cancelled';

  return {
    id: hexId(`order:${seed.status}:${index}`),
    orderNumber: `GG${new Date(placedAt).toISOString().slice(2, 10).replace(/-/g, '')}${String(index + 1).padStart(3, '0')}`,
    status: seed.status,
    shippingPolicy: 'ShipSeparately',
    deliveryMethod: 'ConvenienceStore',
    goodsTotal: { amountMinor: goodsTotalMinor, currency: 'TWD' },
    shippingFee: SHIPPING_FEE_CONVENIENCE,
    grandTotal: { amountMinor: grandTotalMinor, currency: 'TWD' },
    paidAmount: isPaid ? { amountMinor: grandTotalMinor, currency: 'TWD' } : null,
    lines,
    shippingAddress: addresses[0] ?? null,
    recipientName: '王小美',
    recipientPhone: '0912345678',
    convenienceStoreName: '7-ELEVEN 信義門市',
    convenienceStoreAddress: '台北市信義區松仁路 100 號',
    placedAt,
    paymentDueAt: seed.status === 'AwaitingPayment' ? hoursFromNow(2) : null,
    quoteExplain: ['超商取貨一口價 NT$60（ADR-010）。'],
  };
}

export const orders: S['Order'][] = ORDER_SEEDS.map(buildOrder);

export function orderListItemOf(order: S['Order']): S['OrderListItem'] {
  return {
    id: order.id,
    orderNumber: order.orderNumber,
    status: order.status,
    grandTotal: order.grandTotal,
    placedAt: order.placedAt,
    lineCount: order.lines.length,
    thumbnailUrl: null,
  };
}

export const orderListItems: S['OrderListItem'][] = orders.map(orderListItemOf);
