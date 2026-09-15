import type { components } from '@greygray/api-client/storefront';

type Cart = components['schemas']['Cart'];
export type CartIntent = 'add-to-cart' | 'buy-now';

export interface BuyNowSelection {
  readonly skuId: string;
  readonly quantity: number;
}

/** 只有回來的整台購物車能證明「裡面只有這次加入的品項」時才直接結帳。 */
export function buyNowDestination(cart: Cart, selection: BuyNowSelection): '/checkout' | '/cart?from=buy-now' {
  const onlyLine = cart.lines.length === 1 ? cart.lines[0] : undefined;
  return onlyLine?.skuId === selection.skuId && onlyLine.quantity === selection.quantity
    ? '/checkout'
    : '/cart?from=buy-now';
}

interface PendingRef {
  current: boolean;
}

interface ExecuteCartIntentOptions {
  readonly intent: CartIntent;
  readonly selection: BuyNowSelection;
  readonly pendingRef: PendingRef;
  readonly addLine: () => Promise<Cart>;
  readonly onStart: () => void;
  readonly onCartUpdated: (cart: Cart) => void;
  readonly onAdded: () => void;
  readonly onNavigate: (href: '/checkout' | '/cart?from=buy-now') => void;
  readonly onError: (cause: unknown) => void;
}

/** 兩顆按鈕共用的同步鎖；第一個進來的意圖獨占這一次請求與後續動作。 */
export async function executeCartIntent(options: ExecuteCartIntentOptions): Promise<void> {
  if (options.pendingRef.current) return;
  options.pendingRef.current = true;
  options.onStart();

  try {
    const updated = await options.addLine();
    options.onCartUpdated(updated);
    if (options.intent === 'buy-now') {
      options.onNavigate(buyNowDestination(updated, options.selection));
    } else {
      options.onAdded();
    }
  } catch (cause) {
    options.onError(cause);
  } finally {
    options.pendingRef.current = false;
  }
}
