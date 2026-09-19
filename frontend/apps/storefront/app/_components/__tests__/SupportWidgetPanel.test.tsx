/**
 * 沒有 jsdom，所以用 `renderToStaticMarkup`（做法與理由見
 * `(shop)/products/[productId]/__tests__/priceDisplay.test.tsx` 檔頭）。
 * `SupportWidgetPanel` 是純呈現元件（不掛 `BottomSheet`／`createPortal`），
 * 畫什麼完全由 `state` 決定，四種畫面在這裡都測得到。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { SUPPORT_WAIT_MESSAGE } from '../../_lib/supportErrors';
import { initialSupportWidgetState, type SupportFaqGroupLike, type SupportWidgetState } from '../../_lib/supportWidget';
import { SupportWidgetPanel } from '../SupportWidgetPanel';

(globalThis as unknown as { React: typeof React }).React = React;

const GROUPS: readonly SupportFaqGroupLike[] = [
  {
    title: '訂購與付款',
    items: [{ question: '什麼時候可以下單？', answer: '開團期間都可以。' }],
  },
];

const NOOP = () => {};

function render(state: SupportWidgetState) {
  return renderToStaticMarkup(
    <SupportWidgetPanel
      state={state}
      groups={GROUPS}
      onSelectGroup={NOOP}
      onSelectItem={NOOP}
      onBack={NOOP}
      onMarkResolved={NOOP}
      onMarkUnresolved={NOOP}
      onStartOtherQuestion={NOOP}
      onMessageChange={NOOP}
      onContactEmailChange={NOOP}
      onContactPhoneChange={NOOP}
      onSubmit={NOOP}
      onDone={NOOP}
    />,
  );
}

describe('root：常見問題分類 ＋「其他問題」', () => {
  it('畫出每一個 FAQ 分類與「其他問題」', () => {
    const html = render({ ...initialSupportWidgetState, open: true, view: 'root' });
    expect(html).toContain('訂購與付款');
    expect(html).toContain('其他問題');
  });
});

describe('group：分類底下的問題清單', () => {
  it('畫出選中分類的題目', () => {
    const html = render({ ...initialSupportWidgetState, open: true, view: 'group', groupIndex: 0 });
    expect(html).toContain('什麼時候可以下單？');
    expect(html).toContain('返回');
  });
});

describe('answer：答案與「這樣有解決嗎？」', () => {
  it('畫出問題、答案，以及有解決／沒有解決兩個按鈕', () => {
    const html = render({
      ...initialSupportWidgetState,
      open: true,
      view: 'answer',
      groupIndex: 0,
      itemIndex: 0,
    });
    expect(html).toContain('什麼時候可以下單？');
    expect(html).toContain('開團期間都可以。');
    expect(html).toContain('這樣有解決嗎？');
    expect(html).toContain('有解決');
    expect(html).toContain('沒有解決');
  });
});

describe('contactForm：留言表單', () => {
  it('畫出訊息、email、手機三個欄位', () => {
    const html = render({ ...initialSupportWidgetState, open: true, view: 'contactForm', menuPath: ['其他問題'] });
    expect(html).toContain('想告訴我們什麼');
    expect(html).toContain('Email');
    expect(html).toContain('手機');
  });

  it('有 errorMessage 時顯示出來（429 要顯示看得懂的訊息，不是技術錯誤）', () => {
    const html = render({
      ...initialSupportWidgetState,
      open: true,
      view: 'contactForm',
      errorMessage: '你剛剛已經留過言了，我們會儘快回覆。',
    });
    expect(html).toContain('你剛剛已經留過言了，我們會儘快回覆。');
  });

  it('送出中按鈕會停用（避免連點送出兩張工單）', () => {
    const html = render({
      ...initialSupportWidgetState,
      open: true,
      view: 'contactForm',
      message: '哈囉',
      contactEmail: 'a@b.com',
      submitting: true,
    });
    expect(html).toContain('disabled');
  });
});

describe('success：送出後要告訴客人多久會回', () => {
  it('顯示等待訊息，不是只說「已送出」就結束', () => {
    const html = render({ ...initialSupportWidgetState, open: true, view: 'success', ticketId: 'ticket-1' });
    expect(html).toContain(SUPPORT_WAIT_MESSAGE);
    expect(html).toContain('知道了');
  });
});
