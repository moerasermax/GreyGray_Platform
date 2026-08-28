'use client';

/*
 * 結帳頁：配送方式選擇與詢價 → （混合訂單）出貨方式 → 地址／門市 → 送出。
 *
 * **冪等鍵規則（docs/06 FE-4 最重要的一條）**：`checkoutAction` 在這個元件
 * 掛載時只建立一次（`useRef` lazy init）；相同 payload 的整個送出流程含重試都共用
 * 同一把 key，連點五次也只會真正送出一次 HTTP 請求。失敗後若使用者修改欄位，
 * payload 改變就會換 key，避免後端判定同 key 不同內容。邏輯與驗收腳本都在 `_lib`。
 */

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import {
  BottomActionBar,
  Button,
  Card,
  EmptyState,
  ErrorState,
  Field,
  PriceDisplay,
  Skeleton,
  Textarea,
} from '@greygray/ui';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import type { IdempotentAction, PayloadIdempotentAction } from '@greygray/api-client';
import { browserApi } from '../../_lib/apiClient';
import { AddressSelect } from '../_components/AddressSelect';
import { ConvenienceStoreField } from '../_components/ConvenienceStoreField';
import { DeliveryMethodPicker } from '../_components/DeliveryMethodPicker';
import { ExplainDisclosure } from '../_components/ExplainDisclosure';
import { ShippingPolicyPicker } from '../_components/ShippingPolicyPicker';
import { createIdempotentAction, createPayloadIdempotentAction } from '../_lib/idempotentAction';
import { evaluateCheckoutReadiness } from '../_lib/cartRules';
import { describeError, type ErrorDisplay } from '../_lib/errorDisplay';
import { DELIVERY_METHOD_LABEL } from '../_lib/labels';
import { useCart } from '../_lib/useCart';

type S = components['schemas'];

type CheckoutInput = Parameters<typeof api.checkout>[1];

export default function CheckoutPage() {
  const router = useRouter();
  const { cart, loading, error, reload } = useCart();

  const [deliveryMethod, setDeliveryMethod] = useState<S['DeliveryMethod'] | null>(null);
  const [quotingMethod, setQuotingMethod] = useState<S['DeliveryMethod'] | null>(null);
  const [quote, setQuote] = useState<S['QuoteResult'] | null>(null);
  const [quoteError, setQuoteError] = useState<ErrorDisplay | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const quoteRequestSequenceRef = useRef(0);
  const quoteActionsRef = useRef(new Map<S['DeliveryMethod'], IdempotentAction<S['QuoteResult']>>());

  const [shippingPolicy, setShippingPolicy] = useState<S['ShippingPolicy'] | null>(null);
  const [shippingAddressId, setShippingAddressId] = useState<string | null>(null);
  const [convenienceStoreCode, setConvenienceStoreCode] = useState('');
  const [buyerNote, setBuyerNote] = useState('');

  const [addresses, setAddresses] = useState<S['ShippingAddress'][]>([]);
  const [addressesLoading, setAddressesLoading] = useState(false);
  const [addressesError, setAddressesError] = useState<ErrorDisplay | null>(null);
  const addressesFetchedRef = useRef(false);

  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<ErrorDisplay | null>(null);

  // 購物車第一次載入時，如果已經有上一次詢價的結果（例如從購物車頁點過來），沿用它。
  // 只做一次——之後使用者自己選的配送方式不該被 cart 的重新整理蓋掉。
  const quoteInitializedRef = useRef(false);
  useEffect(() => {
    if (cart && !quoteInitializedRef.current) {
      quoteInitializedRef.current = true;
      if (cart.quote) {
        setDeliveryMethod(cart.quote.deliveryMethod);
        setQuote(cart.quote);
      }
    }
  }, [cart]);

  useEffect(() => {
    if (deliveryMethod !== 'HomeDelivery' || addressesFetchedRef.current) return;
    addressesFetchedRef.current = true;
    setAddressesLoading(true);
    api
      .listAddresses(browserApi())
      .then((list) => {
        setAddresses(list);
        const preferred = list.find((a) => a.isDefault) ?? list[0];
        if (preferred) setShippingAddressId((prev) => prev ?? preferred.id);
      })
      .catch((cause: unknown) => setAddressesError(describeError(cause)))
      .finally(() => setAddressesLoading(false));
  }, [deliveryMethod]);

  const checkoutActionRef = useRef<PayloadIdempotentAction<CheckoutInput, S['Order']> | null>(null);
  checkoutActionRef.current ??= createPayloadIdempotentAction((input, idempotencyKey) =>
    api.checkout(browserApi(), input, { idempotencyKey }),
  );

  async function handleSelectDeliveryMethod(method: S['DeliveryMethod']) {
    const requestSequence = ++quoteRequestSequenceRef.current;
    setDeliveryMethod(method);
    setQuotingMethod(method);
    setQuoteError(null);
    try {
      let action = quoteActionsRef.current.get(method);
      if (!action) {
        action = createIdempotentAction((idempotencyKey) =>
          api.quoteCart(browserApi(), { deliveryMethod: method }, { idempotencyKey }),
        );
        quoteActionsRef.current.set(method, action);
      }
      const result = await action.run();
      if (requestSequence !== quoteRequestSequenceRef.current) return;
      setQuote(result);
      setSheetOpen(false);
    } catch (cause) {
      if (requestSequence !== quoteRequestSequenceRef.current) return;
      setQuoteError(describeError(cause));
    } finally {
      if (requestSequence === quoteRequestSequenceRef.current) setQuotingMethod(null);
    }
  }

  async function handleSubmit() {
    if (!cart || !readiness.ready) return;
    setSubmitError(null);
    setSubmitting(true);
    try {
      const input: CheckoutInput = {
        deliveryMethod: deliveryMethod!,
        shippingPolicy: shippingPolicy!,
        shippingAddressId,
        convenienceStoreCode: convenienceStoreCode || null,
        buyerNote: buyerNote || null,
      };
      const order = await checkoutActionRef.current!.run(input);
      router.push(`/payment/${order.id}`);
    } catch (cause) {
      setSubmitError(describeError(cause));
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
        <Skeleton variant="text" className="h-8 w-32" />
        <Skeleton variant="block" className="h-40 w-full" />
        <Skeleton variant="block" className="h-40 w-full" />
      </main>
    );
  }

  if (error) {
    return <ErrorState title={error.title} traceId={error.traceId} onRetry={reload} />;
  }

  if (!cart || cart.lines.length === 0) {
    return (
      <EmptyState
        title="購物車是空的"
        description="結帳前要先把商品加進購物車。"
        action={
          <Link href="/">
            <Button variant="primary">回首頁逛逛</Button>
          </Link>
        }
      />
    );
  }

  const readiness = evaluateCheckoutReadiness({
    cart,
    deliveryMethod,
    shippingPolicy,
    shippingAddressId,
    convenienceStoreCode: convenienceStoreCode || null,
  });

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-6)] pb-[calc(var(--gg-bottom-bar-height)+var(--gg-space-8))]">
      <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">結帳</h1>

      <Card padding="md" className="flex flex-col gap-[var(--gg-space-3)]">
        <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
          <div>
            <p className="text-[length:var(--gg-text-sm)] font-bold text-fg-muted">配送方式</p>
            <p className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">
              {deliveryMethod ? DELIVERY_METHOD_LABEL[deliveryMethod] : '尚未選擇'}
            </p>
          </div>
          <Button variant="secondary" onClick={() => setSheetOpen(true)}>
            {deliveryMethod ? '更換' : '選擇配送方式'}
          </Button>
        </div>

        {quoteError && (
          <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
            {quoteError.title}
          </p>
        )}

        {quote && (
          <div className="flex flex-col gap-[var(--gg-space-2)] border-t border-border-soft pt-[var(--gg-space-3)]">
            <div className="flex items-center justify-between text-[length:var(--gg-text-sm)] text-fg-muted">
              <span>商品小計</span>
              <PriceDisplay amount={quote.goodsTotal} size="sm" />
            </div>
            <div className="flex items-center justify-between text-[length:var(--gg-text-sm)] text-fg-muted">
              <span>運費</span>
              <PriceDisplay amount={quote.shippingFee} size="sm" />
            </div>
            <div className="flex items-center justify-between">
              <span className="font-bold text-fg">含運總額</span>
              <PriceDisplay amount={quote.grandTotal} size="md" />
            </div>
            <ExplainDisclosure items={quote.explain} />
          </div>
        )}
      </Card>

      {cart.hasMixedModes && (
        <Card padding="md">
          <ShippingPolicyPicker value={shippingPolicy} onChange={setShippingPolicy} />
        </Card>
      )}

      {deliveryMethod === 'HomeDelivery' && (
        <Card padding="md" className="flex flex-col gap-[var(--gg-space-2)]">
          <AddressSelect
            addresses={addresses}
            loading={addressesLoading}
            value={shippingAddressId}
            onChange={setShippingAddressId}
          />
          {addressesError && (
            <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
              {addressesError.title}
            </p>
          )}
        </Card>
      )}

      {deliveryMethod === 'ConvenienceStore' && (
        <Card padding="md">
          <ConvenienceStoreField value={convenienceStoreCode} onChange={setConvenienceStoreCode} />
        </Card>
      )}

      <Card padding="md">
        <Field label="給客服的留言" htmlFor="checkout-note" hint="選填，最多 200 字">
          <Textarea
            id="checkout-note"
            value={buyerNote}
            maxLength={200}
            onChange={(e) => setBuyerNote(e.target.value)}
            placeholder="有什麼想提醒我們的嗎？"
          />
        </Field>
      </Card>

      {submitError && (
        <div className="flex flex-col gap-[var(--gg-space-2)] rounded-card border border-danger/30 bg-danger-subtle p-[var(--gg-space-4)]">
          <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
            {submitError.title}
          </p>
          {submitError.traceId && (
            <p className="text-[length:var(--gg-text-xs)] text-fg-muted">參考代碼：{submitError.traceId}</p>
          )}
        </div>
      )}

      <DeliveryMethodPicker
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        value={deliveryMethod}
        onSelect={handleSelectDeliveryMethod}
        quoting={quotingMethod}
      />

      <BottomActionBar>
        <div className="flex flex-1 flex-col">
          {quote ? (
            <PriceDisplay amount={quote.grandTotal} size="lg" />
          ) : (
            <span className="text-[length:var(--gg-text-base)] font-bold text-fg-muted">運費另計</span>
          )}
          {!readiness.ready && readiness.reason && (
            <span className="text-[length:var(--gg-text-xs)] text-danger">{readiness.reason}</span>
          )}
        </div>
        <Button variant="primary" loading={submitting} disabled={!readiness.ready} onClick={handleSubmit}>
          送出訂單
        </Button>
      </BottomActionBar>
    </main>
  );
}
