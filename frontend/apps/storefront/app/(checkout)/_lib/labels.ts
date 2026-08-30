/** 列舉值 → 中文顯示字串。集中在一處，畫面不要各自硬寫一份。 */
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export const DELIVERY_METHOD_LABEL: Record<S['DeliveryMethod'], string> = {
  ConvenienceStore: '超商取貨',
  HomeDelivery: '宅配到府',
  SelfPickup: '自取',
};

/**
 * 這裡不放運費金額——選這一步還沒詢價（`quoteCart` 要等選定方式才打），
 * 契約在這裡沒有 `shippingFee` 可用，金額只能等 `QuoteResult` 回來後用
 * `formatMoney()` 渲染（見 `checkout/page.tsx` 的 `quote.shippingFee`）。
 */
export const DELIVERY_METHOD_HINT: Record<S['DeliveryMethod'], string> = {
  ConvenienceStore: '選定後立即試算運費',
  HomeDelivery: '選定後立即試算運費',
  SelfPickup: '免運費',
};

export const SHIPPING_POLICY_LABEL: Record<S['ShippingPolicy'], string> = {
  ShipSeparately: '現貨先出',
  HoldUntilComplete: '等回國一起出',
};

export const SHIPPING_POLICY_HINT: Record<S['ShippingPolicy'], string> = {
  ShipSeparately: '現貨商品馬上出貨，預購商品回國後再出，運費算兩次。',
  HoldUntilComplete: '等預購商品一起回國後合併出貨，只收一次運費，但要等比較久。',
};
