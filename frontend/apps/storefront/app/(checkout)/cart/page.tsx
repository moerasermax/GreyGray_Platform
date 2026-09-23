'use client';

/*
 * 購物車頁：看商品、改數量、移除。**含運總額顯示規則見 docs/06 FE-4**——
 * 這裡只讀 `cart.quote?.grandTotal`（配送方式選擇與詢價在 `/checkout` 頁做），
 * 還沒選配送方式就顯示「運費另計」，絕對不要顯示 0。
 */

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { BottomActionBar, Button, Card, EmptyState, ErrorState, PriceDisplay, Skeleton } from '@greygray/ui';
import * as api from '@greygray/api-client/endpoints/storefront';
import { PageTopBar } from '../../_components/PageTopBar';
import { browserApi } from '../../_lib/apiClient';
import { usePayloadIdempotency } from '../../_lib/usePayloadIdempotency';
import { publishCart } from '../../_lib/cartCountStore';
import { CartLineRow } from '../_components/CartLineRow';
import { useCart } from '../_lib/useCart';
import { blockingAvailabilityWarning } from '../_lib/cartRules';
import { describeError } from '../_lib/errorDisplay';
import { shouldShowBuyNowNotice } from '../_lib/buyNowNotice';

const BUY_NOW_NOTICE = '購物車裡還有其他商品，或這件商品原本就在購物車裡，請確認品項與數量後再結帳。';

/*
 * #32：這一頁的分頁列被自己的 `BottomActionBar` 擠掉了，畫面上只有「前往結帳」——
 * 有東西的購物車一個出口都沒有（「回首頁逛逛」只在空車那個分支）。
 *
 * 頂部列包在外層而不是塞進下面每一個 return，是因為載入中、失敗、空車、有東西
 * **四種狀態都需要出口**——尤其失敗那一頁，除了重試以外原本哪裡都去不了。
 * 內容原封不動搬進 `CartPageContent`，版面與邏輯零改動。
 */
export default function CartPage() {
  const [showBuyNowNotice, setShowBuyNowNotice] = useState(false);

  useEffect(() => {
    setShowBuyNowNotice(shouldShowBuyNowNotice(window.location.search));
  }, []);

  return (
    <>
      <PageTopBar title="購物車" />
      {showBuyNowNotice && (
        <p
          role="status"
          className="mx-auto mt-[var(--gg-space-3)] max-w-[var(--gg-container-max)] px-[var(--gg-space-4)] text-[length:var(--gg-text-sm)] text-primary-text"
        >
          {BUY_NOW_NOTICE}
        </p>
      )}
      <CartPageContent />
    </>
  );
}

function CartPageContent() {
  const idempotency = usePayloadIdempotency();
  const { cart, loading, error, reload, setCart } = useCart();
  const [busyLineId, setBusyLineId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  /*
   * 把最新的購物車推給分頁列的徽章。放在這裡而不是每個 handler 裡，
   * 是因為載入完成與改數量／移除都只是 `cart` 換了一份，一個 effect 全包。
   *
   * `cart` 還是 `null`（載入中或失敗）時**不推**：store 的 `null` 意思是
   * 「不知道幾件」，拿它蓋掉一個已經知道的數字，只會讓徽章在載入中閃掉一次。
   */
  useEffect(() => {
    if (cart) publishCart(cart);
  }, [cart]);

  async function updateQuantity(lineId: string, quantity: number) {
    setBusyLineId(lineId);
    setActionError(null);
    try {
      const payload = { kind: 'update', lineId, quantity };
      const updated = await api.updateCartLine(browserApi(), lineId, { quantity }, { idempotencyKey: idempotency.current(payload) });
      idempotency.complete();
      setCart(updated);
    } catch (cause) {
      setActionError(describeError(cause).title);
    } finally {
      setBusyLineId(null);
    }
  }

  async function removeLine(lineId: string) {
    setBusyLineId(lineId);
    setActionError(null);
    try {
      const payload = { kind: 'remove', lineId };
      const updated = await api.removeCartLine(browserApi(), lineId, { idempotencyKey: idempotency.current(payload) });
      idempotency.complete();
      setCart(updated);
    } catch (cause) {
      setActionError(describeError(cause).title);
    } finally {
      setBusyLineId(null);
    }
  }

  if (loading) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
        <Skeleton variant="text" className="h-8 w-32" />
        <Skeleton variant="block" className="h-28 w-full" />
        <Skeleton variant="block" className="h-28 w-full" />
      </main>
    );
  }

  if (error) {
    return <ErrorState title={error.title} traceId={error.traceId} onRetry={reload} />;
  }

  if (!cart) return null;

  if (cart.lines.length === 0) {
    return (
      <EmptyState
        title="購物車是空的"
        description="去逛逛，把喜歡的商品加進購物車吧。"
        action={
          <Link href="/">
            <Button variant="primary">回首頁逛逛</Button>
          </Link>
        }
      />
    );
  }

  const warning = blockingAvailabilityWarning(cart);

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-6)] pb-[calc(var(--gg-bottom-bar-height)+var(--gg-space-8))]">
      <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">購物車</h1>

      {actionError && (
        <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
          {actionError}
        </p>
      )}

      <Card padding="md">
        {cart.lines.map((line) => (
          <CartLineRow
            key={line.id}
            line={line}
            busy={busyLineId === line.id}
            onQuantityChange={(quantity) => updateQuantity(line.id, quantity)}
            onRemove={() => removeLine(line.id)}
          />
        ))}
      </Card>

      {cart.hasMixedModes && (
        <p className="rounded-card bg-surface-sunken p-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] leading-[var(--gg-leading-normal)] text-fg-muted">
          這張購物車同時有現貨與預購商品，結帳時要選出貨方式（現貨先出或等回國一起出）。
        </p>
      )}

      <BottomActionBar>
        {/* 寬螢幕上金額靠到按鈕旁邊（`sm:ml-auto`），不再各據列的一端；手機維持金額撐滿、按鈕靠右。 */}
        <div className="flex min-w-0 flex-1 flex-col break-words sm:ml-auto sm:flex-none sm:items-end sm:text-right">
          {cart.quote ? (
            <PriceDisplay amount={cart.quote.grandTotal} size="lg" />
          ) : (
            <span className="text-[length:var(--gg-text-base)] font-bold text-fg-muted">運費另計</span>
          )}
          {warning && <span className="text-[length:var(--gg-text-xs)] text-danger">{warning}</span>}
        </div>
        {warning ? (
          <Button variant="primary" disabled>
            前往結帳
          </Button>
        ) : (
          <Link href="/checkout" className="shrink-0">
            <Button variant="primary">前往結帳</Button>
          </Link>
        )}
      </BottomActionBar>
    </main>
  );
}
