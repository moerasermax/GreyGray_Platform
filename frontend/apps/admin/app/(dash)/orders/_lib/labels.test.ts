import { describe, expect, it } from 'vitest';
import {
  deliveryMethodLabel,
  fulfillmentModeLabel,
  orderLineStatusLabel,
  orderLineStatusTone,
  orderStatusLabel,
  orderStatusTone,
  orderCancellationSourceLabel,
  manualRefundStatusLabel,
  manualRefundStatusTone,
  paymentMethodLabel,
  paymentProviderLabel,
  paymentStatusLabel,
  paymentStatusTone,
  refundDestinationHint,
  refundDestinationLabel,
  refundedAmountText,
  shippingPolicyLabel,
} from './labels';

/**
 * 這一組測試守的是 `docs/06-前端工作包.md` 鐵則 5：
 * **enum 一律容忍未知值——後端新增列舉成員不算破壞性變更（`docs/05` §1）。**
 *
 * 未知值要退回顯示原始字串，不可以是空字串、不可以丟例外。
 * 這條規則只靠 code review 守不住：新增一個 `case` 忘了 `default`，
 * 型別檢查不會抱怨（回傳型別仍然是 string），畫面要等後端真的加了新狀態才爆。
 */

/** 訂單狀態九個（`docs/02-事件與狀態機.md` 的訂單狀態機）。 */
const ORDER_STATUSES = [
  'AwaitingPayment',
  'PaidAwaitingClose',
  'ClosedAwaitingDeparture',
  'Purchasing',
  'GoodsReceived',
  'ReadyToShip',
  'Shipped',
  'Completed',
  'Cancelled',
] as const;

const UNKNOWN = 'SomeStatusTheBackendAddedLater';

describe('orderStatusLabel', () => {
  it('九個狀態都有中文標籤，而且不會是原始英文', () => {
    for (const status of ORDER_STATUSES) {
      const label = orderStatusLabel(status);
      expect(label, status).not.toBe('');
      expect(label, status).not.toBe(status);
    }
  });

  it('九個狀態的標籤互不重複——重複的話畫面上分不出來', () => {
    const labels = ORDER_STATUSES.map(orderStatusLabel);
    expect(new Set(labels).size).toBe(ORDER_STATUSES.length);
  });

  it('未知狀態退回原始字串，不是空字串也不丟例外', () => {
    expect(orderStatusLabel(UNKNOWN)).toBe(UNKNOWN);
  });
});

describe('orderStatusTone', () => {
  it('未知狀態退回 neutral，不丟例外', () => {
    expect(orderStatusTone(UNKNOWN)).toBe('neutral');
  });

  it('待付款是 warning——那是唯一需要客服追的狀態', () => {
    expect(orderStatusTone('AwaitingPayment')).toBe('warning');
  });

  it('Cancelled 用 neutral 不用 danger——取消是正常結局，不是錯誤', () => {
    expect(orderStatusTone('Cancelled')).toBe('neutral');
  });
});

describe('orderLineStatus', () => {
  it('未知值退回原始字串與 neutral', () => {
    expect(orderLineStatusLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(orderLineStatusTone(UNKNOWN)).toBe('neutral');
  });

  it('Unavailable 有自己的標籤——缺貨退款的 line 要一眼看得出來', () => {
    const label = orderLineStatusLabel('Unavailable');
    expect(label).not.toBe('Unavailable');
    expect(label).not.toBe('');
  });
});

/**
 * `refundedAmount` 是 `Money | null`。**null 不是 0**——沒退款不能顯示「$0」，
 * 這是 FE-9 派工書明講的鐵則（`docs/12` §4 FE-9）。
 */
describe('refundedAmountText', () => {
  it('null 回傳「—」，不是「$0」也不是空字串', () => {
    const text = refundedAmountText(null);
    expect(text).toBe('—');
    expect(text).not.toContain('0');
  });

  it('undefined 也回傳「—」——契約裡這個欄位是 optional', () => {
    expect(refundedAmountText(undefined)).toBe('—');
  });

  it('有金額時用 formatMoney 格式化，不是自己組字串', () => {
    expect(refundedAmountText({ amountMinor: 89_000, currency: 'TWD' })).toContain('890');
  });
});

describe('paymentStatus', () => {
  it('未知值退回原始字串與 neutral', () => {
    expect(paymentStatusLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(paymentStatusTone(UNKNOWN)).toBe('neutral');
  });

  it('InstructionsIssued 顯示已取號待繳費且是 warning', () => {
    expect(paymentStatusLabel('InstructionsIssued')).toBe('已取號待繳費');
    expect(paymentStatusTone('InstructionsIssued')).toBe('warning');
  });
});

describe('ADR-044 新增標籤', () => {
  it('四種付款方式都有中文，未知值保留原字', () => {
    expect(paymentMethodLabel('CreditCard')).toBe('信用卡');
    expect(paymentMethodLabel('Atm')).toBe('ATM 轉帳');
    expect(paymentMethodLabel('ConvenienceStoreCode')).toBe('超商代碼');
    expect(paymentMethodLabel('Barcode')).toBe('超商條碼');
    expect(paymentMethodLabel(UNKNOWN)).toBe(UNKNOWN);
  });

  it('三種取消來源都有中文，未知值保留原字', () => {
    expect(orderCancellationSourceLabel('Customer')).toBe('客人自行取消');
    expect(orderCancellationSourceLabel('Staff')).toBe('後台取消');
    expect(orderCancellationSourceLabel('PaymentExpired')).toBe('逾期未付款自動取消');
    expect(orderCancellationSourceLabel(UNKNOWN)).toBe(UNKNOWN);
  });

  it('人工退款狀態有中文與 tone，未知值回原字加 neutral', () => {
    expect(manualRefundStatusLabel('Pending')).toBe('待人工退款');
    expect(manualRefundStatusLabel('Completed')).toBe('已全額登記');
    expect(manualRefundStatusLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(manualRefundStatusTone(UNKNOWN)).toBe('neutral');
  });
});

describe('其餘 enum 的未知值處理', () => {
  it('全部退回原始字串', () => {
    expect(fulfillmentModeLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(deliveryMethodLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(shippingPolicyLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(paymentProviderLabel(UNKNOWN)).toBe(UNKNOWN);
    expect(refundDestinationLabel(UNKNOWN)).toBe(UNKNOWN);
  });
});

/**
 * 退款去向是**會動到錢**的選擇，而且兩個選項對客人的實收金額不同。
 * `docs/06` FE-8 要求 UI 說明差別，說明文字寫錯等於誤導客服與客人。
 */
describe('refundDestination', () => {
  it('兩個選項都有標籤，而且分得出來', () => {
    expect(refundDestinationLabel('StoredValue')).toBe('退成儲值金');
    expect(refundDestinationLabel('OriginalPaymentMethod')).toBe('原路退回');
  });

  it('儲值金的說明要講到「零手續費」——那是預設選它的理由', () => {
    expect(refundDestinationHint('StoredValue')).toContain('零手續費');
  });

  it('原路退回的說明要講到「手續費」——客人實收會變少，不講清楚會有客訴', () => {
    expect(refundDestinationHint('OriginalPaymentMethod')).toContain('手續費');
  });

  it('兩個說明不可以一樣，否則等於沒說明', () => {
    expect(refundDestinationHint('StoredValue')).not.toBe(
      refundDestinationHint('OriginalPaymentMethod'),
    );
  });

  it('未知值的說明回空字串——寧可不顯示，也不要顯示錯的金流說明', () => {
    expect(refundDestinationHint(UNKNOWN)).toBe('');
  });
});
