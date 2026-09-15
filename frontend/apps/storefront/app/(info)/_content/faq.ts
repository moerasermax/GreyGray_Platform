/**
 * 常見問題的文案資料。頁面只負責排版，字放這裡——
 * 測試（`__tests__/infoPages.test.ts`）讀的是這個檔案，不是渲染結果。
 *
 * 內容照派工書 §1.4 逐字照用；經 Codex 覆驗更正三處後的版本：
 * 拿掉「七天鑑賞期」（ADR-025：法律適用由老闆判斷，前端不能替他做判斷）、
 * 「送出訂單」不等於「訂單成立就是已付款」、出貨不承諾「就」馬上發生。
 */

export interface FaqItem {
  readonly question: string;
  readonly answer: string;
}

export interface FaqGroup {
  readonly title: string;
  readonly items: readonly FaqItem[];
}

export const FAQ_GROUPS: readonly FaqGroup[] = [
  {
    title: '訂購與付款',
    items: [
      {
        question: '預購什麼時候可以下單？',
        answer: '每一團都有截團時間。只有在開團期間、還沒截團之前才能送出訂單。',
      },
      {
        question: '同一張訂單同時有現貨和預購，要怎麼出貨？',
        answer: '下單時可以選「現貨先出貨」（會付兩次運費），或「等預購回國一起出貨」（只付一次運費）。',
      },
      {
        question: '可以用哪些方式付款？',
        answer: '透過綠界科技（ECPay）線上付款，實際可用的付款方式以付款頁顯示為準。',
      },
      {
        question: '送出訂單之後什麼時候要付款？',
        answer: '送出訂單後會先建立一筆待付款訂單，請在訂單上顯示的付款期限內完成付款。',
      },
      {
        question: '一定要加入會員才能買嗎？',
        answer: '可以先把商品放進購物車，送出訂單時才需要登入。',
      },
    ],
  },
  {
    title: '運送與取貨',
    items: [
      {
        question: '運費怎麼算？',
        answer: '一口價：超商取貨 NT$60、宅配到府 NT$120、自取 NT$0。',
      },
      {
        question: '現貨付款後多久出貨？',
        answer: '現貨已經在倉庫裡，付款確認後會安排出貨；實際時間請聯絡客服。',
      },
      {
        question: '預購的商品什麼時候出貨？',
        answer: '截團後我們出國採購，回國後出貨。',
      },
    ],
  },
  {
    title: '取消與退款',
    items: [
      {
        question: '還沒付款的訂單可以自己取消嗎？',
        answer: '可以，到「我的 → 我的訂單」就能取消。',
      },
      {
        question: '已經付款的訂單想取消怎麼辦？',
        answer: '請聯絡客服協助處理。',
      },
      {
        question: '預購有商品沒買到怎麼辦？',
        answer: '沒買到的部分會退款，買到的照常出貨。退款會退回原本的付款方式。',
      },
      {
        question: '收到商品後可以退換嗎？',
        answer: '退換貨的條件請聯絡客服確認。',
      },
    ],
  },
  {
    title: '會員',
    items: [
      {
        question: '怎麼註冊會員？',
        answer: '用手機號碼加密碼就能註冊和登入。',
      },
      {
        question: '舊平台的帳號還能用嗎？',
        answer: '舊平台已經停用，需要重新註冊；舊平台的訂單無法查詢。',
      },
    ],
  },
];
