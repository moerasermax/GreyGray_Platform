'use client';

/*
 * 結帳頁：配送方式選擇與詢價 → （混合訂單）出貨方式 → 地址／門市 → 送出。
 *
 * **冪等鍵規則（docs/06 FE-4 最重要的一條）**：`checkoutAction` 在這個元件
 * 掛載時只建立一次（`useRef` lazy init）；相同 payload 的整個送出流程含重試都共用
 * 同一把 key，連點五次也只會真正送出一次 HTTP 請求。失敗後若使用者修改欄位，
 * payload 改變就會換 key，避免後端判定同 key 不同內容。邏輯與驗收腳本都在 `_lib`。
 *
 * **超商門市（ADR-038）**：選門市要整頁離開去 7-ELEVEN 電子地圖再回來，
 * 進頁初始化與錯誤分支都在 `_lib/cvsSelection.ts`。
 */

import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useRef, useState } from 'react';
import {
  BottomActionBar,
  Button,
  Card,
  EmptyState,
  ErrorState,
  Field,
  Input,
  PriceDisplay,
  Skeleton,
  Textarea,
} from '@greygray/ui';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { type IdempotentAction, type PayloadIdempotentAction } from '@greygray/api-client';
import { PageTopBar } from '../../_components/PageTopBar';
import { browserApi } from '../../_lib/apiClient';
import { loginHref } from '../../_lib/auth';
import { AddressSelect } from '../_components/AddressSelect';
import { ConvenienceStoreField } from '../_components/ConvenienceStoreField';
import { DeliveryMethodPicker } from '../_components/DeliveryMethodPicker';
import { ExplainDisclosure } from '../_components/ExplainDisclosure';
import { ShippingPolicyPicker } from '../_components/ShippingPolicyPicker';
import { createIdempotentAction, createPayloadIdempotentAction } from '../_lib/idempotentAction';
import { evaluateCheckoutReadiness } from '../_lib/cartRules';
import {
  clearCheckoutDraft,
  clearOtherCheckoutDrafts,
  loadCheckoutDraft,
  saveCheckoutDraft,
  type CheckoutDraft,
} from '../_lib/checkoutDraft';
import {
  checkoutStoreSelectionId,
  clearCvsSelection,
  handleCheckoutFailure,
  loadCvsSelection,
  mergeCheckoutEntry,
  parseCvsReturn,
  startCvsMapSession,
} from '../_lib/cvsSelection';
import { describeError, type ErrorDisplay } from '../_lib/errorDisplay';
import { DELIVERY_METHOD_LABEL } from '../_lib/labels';
import { checkoutRecipientPayload } from '../_lib/recipientForm';
import { useCart } from '../_lib/useCart';

type S = components['schemas'];

type CheckoutInput = Parameters<typeof api.checkout>[1];

/** 購物車載入中與 Suspense fallback 共用同一組——從地圖回來時畫面不會短暫全白。 */
const CHECKOUT_SKELETON = (
  <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
    <Skeleton variant="text" className="h-8 w-32" />
    <Skeleton variant="block" className="h-40 w-full" />
    <Skeleton variant="block" className="h-40 w-full" />
  </main>
);

/*
 * #32：同 `cart/page.tsx`——分頁列被自己的 `BottomActionBar` 擠掉，
 * 有東西時畫面上只有「送出訂單」，返回不了也離不開。
 * 返回指向 `/cart` 而不是 `/`：結帳的上一步就是購物車（見 `_lib/topBar.ts`）。
 * 四種狀態都要有出口，所以包在外層；內容原封不動搬進 `CheckoutPageContent`。
 *
 * `useSearchParams()`（讀地圖回程的 `cvsSelection`）要求 Suspense 邊界，否則 `next build`
 * 靜態化這一頁時會報錯（前例：`(account)/login/page.tsx`）。頂部列留在邊界外面，出口一直都在。
 */
export default function CheckoutPage() {
  return (
    <>
      <PageTopBar title="結帳" />
      <Suspense fallback={CHECKOUT_SKELETON}>
        <CheckoutPageContent />
      </Suspense>
    </>
  );
}

function CheckoutPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
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
  const [buyerNote, setBuyerNote] = useState('');

  // ADR-039：超商取貨專用，結帳頁不收宅配的收件人（後端從地址簿抄）。
  const [recipientName, setRecipientName] = useState('');
  const [recipientPhone, setRecipientPhone] = useState('');

  const [convenienceStoreSelectionId, setConvenienceStoreSelectionId] = useState<string | null>(null);
  const [cvsSelection, setCvsSelection] = useState<S['CvsStoreSelection'] | null>(null);
  const [cvsSelectionLoading, setCvsSelectionLoading] = useState(false);
  const [cvsSelectionError, setCvsSelectionError] = useState<string | null>(null);
  const cvsSelectionRequestRef = useRef(0);

  const [addresses, setAddresses] = useState<S['ShippingAddress'][]>([]);
  const [addressesLoading, setAddressesLoading] = useState(false);
  const [addressesError, setAddressesError] = useState<ErrorDisplay | null>(null);
  const addressesFetchedRef = useRef(false);

  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<ErrorDisplay | null>(null);

  /*
   * 購物車第一次載入時做兩件事，只做一次——之後使用者自己選的配送方式
   * 不該被 cart 的重新整理蓋掉。
   *
   * ① 還原整頁離開前填的東西（被 401 彈走、或去 7-ELEVEN 電子地圖），並接上地圖的回程參數。
   *    **固定順序、同一個步驟**：讀草稿 → 解析回程 → 純函式合併 → 一次設定 state
   *    → 存回完整草稿 → 清網址 → 讀票。不要拆成好幾個 effect 靠執行順序——
   *    那樣會把草稿蓋成空的、或在 `router.replace` 之後把票弄丟。
   * ② 沿用上一次詢價的結果（例如從購物車頁點過來）。
   *
   * **順序：草稿優先。** 草稿裡的配送方式是使用者最後一次自己選的，
   * 而 `cart.quote` 只是伺服器記得的上一次詢價；兩者不同時要以使用者為準，
   * 並且**重新詢價**（運費由 `deliveryMethod` 決定，不能沿用別種方式的金額）。
   * 重新詢價走的是頁面既有的 `handleSelectDeliveryMethod`，不另外算一份。
   */
  const quoteInitializedRef = useRef(false);
  useEffect(() => {
    if (!cart || quoteInitializedRef.current) return;
    quoteInitializedRef.current = true;

    // 1. 讀草稿。換過車之後，上一張車的草稿就沒有意義了，順手掃掉。
    const draft = loadCheckoutDraft(cart.id);
    clearOtherCheckoutDrafts(cart.id);
    // 2. 解析回程參數。
    const cvsReturn = parseCvsReturn(searchParams);
    // 3. 合併。
    const entry = mergeCheckoutEntry(draft, cvsReturn);
    // 4. 一次設定 state（配送方式在下面跟詢價一起設）。
    setShippingPolicy(entry.draft.shippingPolicy);
    setShippingAddressId(entry.draft.shippingAddressId);
    setBuyerNote(entry.draft.buyerNote);
    setRecipientName(entry.draft.recipientName);
    setRecipientPhone(entry.draft.recipientPhone);
    setConvenienceStoreSelectionId(entry.draft.convenienceStoreSelectionId);
    setCvsSelectionError(entry.errorMessage);
    // 5. 存回合併後的完整草稿：之後重新整理，票還在。
    if (entry.shouldSaveDraft) saveCheckoutDraft(cart.id, entry.draft);
    // 6. 清網址：票與錯誤代碼不留在網址與瀏覽紀錄裡。
    if (entry.shouldClearUrl) router.replace('/checkout');
    // 7. 讀票。
    if (entry.draft.convenienceStoreSelectionId) void readCvsSelection(entry.draft.convenienceStoreSelectionId);

    const restored = entry.draft.deliveryMethod;
    if (restored && cart.quote?.deliveryMethod !== restored) {
      void handleSelectDeliveryMethod(restored);
    } else if (cart.quote) {
      setDeliveryMethod(cart.quote.deliveryMethod);
      setQuote(cart.quote);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  function currentDraft(): CheckoutDraft {
    return {
      deliveryMethod,
      shippingPolicy,
      shippingAddressId,
      convenienceStoreSelectionId,
      recipientName,
      recipientPhone,
      buyerNote,
    };
  }

  /*
   * 選店票失效（讀票 404、送出 422）：**只清選店票**，state 與草稿都清，其他欄位不動。
   * 草稿從 storage 讀回來再清，不從 state 組——這支也會在進頁那個 effect 的閉包裡被叫到，
   * 那時候 state 還是初始值，照 state 存會把剛合併好的草稿蓋成空的。
   * 不准用 `clearCheckoutDraft`：那會連留言一起刪。
   */
  function clearStoreSelection() {
    setConvenienceStoreSelectionId(null);
    setCvsSelection(null);
    if (!cart) return;
    const stored = loadCheckoutDraft(cart.id);
    if (stored) saveCheckoutDraft(cart.id, clearCvsSelection(stored));
  }

  function readCvsSelection(selectionId: string) {
    const requestSequence = ++cvsSelectionRequestRef.current;
    return loadCvsSelection(selectionId, {
      getSelection: (id) => api.getCvsSelection(browserApi(), id),
      isCurrent: () => requestSequence === cvsSelectionRequestRef.current,
      setLoading: setCvsSelectionLoading,
      setSelection: setCvsSelection,
      setError: setCvsSelectionError,
      clearSelectionId: clearStoreSelection,
    });
  }

  /** 前往地圖是整頁離開：先把目前完整的狀態存成草稿，再開票。 */
  function handleStartCvsMap() {
    return startCvsMapSession({
      saveDraft: () => {
        if (cart) saveCheckoutDraft(cart.id, currentDraft());
      },
      createSession: () => api.createCvsMapSession(browserApi(), {}),
    });
  }

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
        // ADR-030：單一模式送 `null` 是對的，後端依 line 組成推導；只有混合購物車才必填。
        shippingPolicy,
        shippingAddressId,
        // ADR-038：只送選店票，不送 `convenienceStoreCode`；只有超商取貨才送。
        convenienceStoreSelectionId: checkoutStoreSelectionId(deliveryMethod, convenienceStoreSelectionId),
        // ADR-039：只有超商取貨才送收件人姓名手機；宅配的收件人以地址簿為準。
        ...checkoutRecipientPayload(deliveryMethod, recipientName, recipientPhone),
        buyerNote: buyerNote || null,
      };
      const order = await checkoutActionRef.current!.run(input);
      // 單子已經成立，草稿沒有用了——留著只會在下一張車上冒出來。
      clearCheckoutDraft(cart.id);
      /*
       * **成功之後不要把 submitting 放掉。**
       * `router.push` 是非同步的，如果在這裡 `finally { setSubmitting(false) }`，
       * 按鈕會在導向完成前就解除 disabled——而那個時候冪等鍵已經因為成功而輪替過了，
       * 使用者在那個空隙再點一下就是**第二張訂單**。
       * 實測：真實速度連點五次會送出 3 次請求、產生 2 把不同的鍵。
       * 失敗才要放開，讓人能用同一把鍵重試。
       */
      router.push(`/payment/${order.id}`);
    } catch (cause) {
      handleCheckoutFailure(cause, {
        /*
         * **401 要給一條去登入的路，不能只顯示錯誤訊息。**
         * 匿名訪客整段流程都走得到這裡（加入購物車、詢價、選地址、選門市都不需要登入），
         * 到「送出訂單」才撞牆——原本畫面上只會多一行 problem title，
         * 人被留在結帳頁，看不出下一步是什麼。這是 #30／#32 的第三種形狀：
         * 路是有的，但走到一半被彈開之後回不去。
         *
         * 回程寫死 `/checkout`：購物車 cookie 不受登入影響，登入完回來東西還在。
         * `submitting` 不放掉——導向是非同步的，放掉會讓人在空隙裡再按一次。
         *
         * **走之前先把已填的東西存起來**（含選店票、收件人姓名手機）。只存使用者自己填的欄位，
         * 鍵含 cart id，`sessionStorage`（分頁關掉就消失）。
         */
        onUnauthorized: () => {
          saveCheckoutDraft(cart.id, currentDraft());
          router.push(loginHref('/checkout'));
        },
        clearSelectionId: clearStoreSelection,
        setStoreError: setCvsSelectionError,
        setSubmitError,
        setSubmitting,
      });
    }
  }

  if (loading) {
    return CHECKOUT_SKELETON;
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

  // 畫面上的門市必須是目前這張票讀回來的——票換了或清了，舊門市就不算。
  const loadedCvsSelection =
    cvsSelection && cvsSelection.selectionId === convenienceStoreSelectionId ? cvsSelection : null;

  const readiness = evaluateCheckoutReadiness({
    cart,
    deliveryMethod,
    shippingPolicy,
    shippingAddressId,
    convenienceStoreSelectionId,
    convenienceStoreSelection: loadedCvsSelection,
    recipientName,
    recipientPhone,
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
        <Card padding="md" className="flex flex-col gap-[var(--gg-space-4)]">
          <ConvenienceStoreField
            selection={loadedCvsSelection}
            loading={cvsSelectionLoading}
            error={cvsSelectionError}
            onStart={handleStartCvsMap}
          />

          <div className="flex flex-col gap-[var(--gg-space-3)] border-t border-border-soft pt-[var(--gg-space-3)]">
            <Field
              label="收件人姓名"
              htmlFor="checkout-recipient-name"
              required
              hint="超商取貨時姓名要跟證件一致，不然超商不給領。"
            >
              <Input
                id="checkout-recipient-name"
                maxLength={50}
                value={recipientName}
                onChange={(e) => setRecipientName(e.target.value)}
              />
            </Field>
            <Field label="收件人手機" htmlFor="checkout-recipient-phone" required>
              <Input
                id="checkout-recipient-phone"
                type="tel"
                inputMode="numeric"
                maxLength={20}
                value={recipientPhone}
                onChange={(e) => setRecipientPhone(e.target.value)}
              />
            </Field>
          </div>
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
