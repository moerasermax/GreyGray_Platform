import type { components } from '@greygray/api-client/storefront';
import { Badge, Card, PriceDisplay, Thumbnail } from '@greygray/ui';
import { mixedOrderShippingMessage } from '../../../(checkout)/_lib/paymentResultSummary';
import { deliveryMethodLabel, orderLineStatusLabel, shippingPolicyLabel } from '../../_lib/orderStatus';
import { recipientDisplayOf } from '../_lib/recipientDisplay';

type S = components['schemas'];
type Order = S['Order'];
type Money = S['Money'];

export interface OrderSummaryProps {
  order: Order;
  variant: 'detail' | 'confirmation';
}

export function OrderSummary({ order, variant }: OrderSummaryProps) {
  if (variant === 'detail') {
    return (
      <>
        <ProductCard order={order} variant="detail" />
        <DeliveryCard order={order} variant="detail" />
      </>
    );
  }

  return (
    <div className="flex w-full flex-col gap-[var(--gg-space-5)] text-left">
      <ProductCard order={order} variant="confirmation" />
      <DeliveryCard order={order} variant="confirmation" />
    </div>
  );
}

function ProductCard({ order, variant }: OrderSummaryProps) {
  const confirmation = variant === 'confirmation';

  return (
    <Card className="flex flex-col gap-[var(--gg-space-4)]">
      <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">商品明細</h2>
      <div className="flex flex-col gap-[var(--gg-space-4)]">
        {order.lines.map((line) =>
          confirmation ? <ConfirmationLine key={line.id} line={line} /> : <DetailLine key={line.id} line={line} />,
        )}
      </div>
      <div className="flex flex-col gap-[var(--gg-space-1)] border-t border-border-soft pt-[var(--gg-space-3)]">
        <MoneyRow label="商品小計" amount={order.goodsTotal} />
        <MoneyRow label="運費" amount={order.shippingFee} />
        <MoneyRow label="含運總額" amount={order.grandTotal} emphasize />
        {order.paidAmount && <MoneyRow label="已付金額" amount={order.paidAmount} />}
      </div>
    </Card>
  );
}

function DetailLine({ line }: { line: S['OrderLine'] }) {
  return (
    <div className="flex flex-col gap-[var(--gg-space-1)] border-b border-border-soft pb-[var(--gg-space-3)] last:border-b-0 last:pb-0">
      <div className="flex items-start justify-between gap-[var(--gg-space-3)]">
        <div>
          <p className="font-bold text-fg">{line.name}</p>
          {line.variantName && (
            <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{line.variantName}</p>
          )}
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
            {orderLineStatusLabel(line.status)}・數量 {line.quantity}
          </p>
        </div>
        <PriceDisplay amount={line.lineTotal} size="sm" />
      </div>
      <UnavailableNotice line={line} />
    </div>
  );
}

function ConfirmationLine({ line }: { line: S['OrderLine'] }) {
  return (
    <div className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-x-[var(--gg-space-3)] border-b border-border-soft pb-[var(--gg-space-3)] last:border-b-0 last:pb-0">
      <span className="relative flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-[var(--gg-radius-sm)] border border-border-soft bg-surface-sunken">
        {!line.imageUrl && (
          <span aria-hidden className="text-[length:var(--gg-text-xs)] text-fg-muted">
            無圖
          </span>
        )}
        <Thumbnail src={line.imageUrl} alt="" sizes="48px" />
      </span>

      <div className="flex min-w-0 flex-col gap-[var(--gg-space-1)]">
        <p className="font-bold text-fg">{line.name}</p>
        {line.variantName && (
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{line.variantName}</p>
        )}
        <p className="flex flex-wrap items-center gap-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] text-fg-muted">
          <Badge variant={line.mode} label={line.mode === 'Stock' ? '現貨' : '預購'} />
          <span>數量 {line.quantity}</span>
        </p>
        <UnavailableNotice line={line} />
      </div>

      <span className="shrink-0 whitespace-nowrap text-right">
        <PriceDisplay amount={line.lineTotal} size="sm" />
      </span>
    </div>
  );
}

function UnavailableNotice({ line }: { line: S['OrderLine'] }) {
  if (line.status !== 'Unavailable') return null;

  return (
    <p className="flex flex-wrap items-center gap-[var(--gg-space-1)] text-[length:var(--gg-text-sm)] text-warning-text">
      <span>這項商品現場缺貨，已退款</span>
      {line.refundedAmount && <PriceDisplay amount={line.refundedAmount} size="sm" />}
      <span>，其餘品項照常出貨。</span>
    </p>
  );
}

function DeliveryCard({ order, variant }: OrderSummaryProps) {
  const recipient = recipientDisplayOf(order);
  const confirmationMessage = variant === 'confirmation' ? mixedOrderShippingMessage(order) : null;

  return (
    <Card className="flex flex-col gap-[var(--gg-space-2)]">
      <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">配送方式</h2>
      <p className="text-[length:var(--gg-text-sm)] text-fg">{deliveryMethodLabel(order.deliveryMethod)}</p>
      {variant === 'detail' && (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{shippingPolicyLabel(order.shippingPolicy)}</p>
      )}
      {confirmationMessage && (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{confirmationMessage}</p>
      )}
      {/*
        ADR-039：優先用訂單本身的快照（下單當時凍結，不會跟著地址簿變）；
        快照為 null 才退回目前地址簿（ADR-039 之前的舊訂單），並標示那是目前的資料，
        不是下單當時凍結的值——見 `_lib/recipientDisplay.ts`。
      */}
      {recipient && (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          收件人：{recipient.recipientName}・{recipient.recipientPhone}
          {recipient.isFallback && (
            <span className="text-[length:var(--gg-text-xs)]">（舊訂單，顯示目前地址簿的收件資訊）</span>
          )}
        </p>
      )}
      {order.shippingAddress && (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          {order.shippingAddress.postalCode} {order.shippingAddress.city}
          {order.shippingAddress.district}
          {order.shippingAddress.streetAddress}
        </p>
      )}
      {order.convenienceStoreName && (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">取貨門市：{order.convenienceStoreName}</p>
      )}
      {order.convenienceStoreAddress && (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{order.convenienceStoreAddress}</p>
      )}
    </Card>
  );
}

function MoneyRow({ label, amount, emphasize }: { label: string; amount: Money; emphasize?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={emphasize ? 'font-bold text-fg' : 'text-[length:var(--gg-text-sm)] text-fg-muted'}>
        {label}
      </span>
      <PriceDisplay amount={amount} size={emphasize ? 'md' : 'sm'} />
    </div>
  );
}
