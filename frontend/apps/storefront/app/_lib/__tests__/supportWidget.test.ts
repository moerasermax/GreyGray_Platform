import { describe, expect, it } from 'vitest';
import {
  OTHER_QUESTION_MENU_PATH_ENTRY,
  initialSupportWidgetState,
  supportWidgetReducer,
  type SupportFaqGroupLike,
} from '../supportWidget';

/**
 * 用自己的假資料而不是真的 `FAQ_GROUPS`——這樣 FE-36 改 FAQ 文案不會讓這支測試跟著紅，
 * 狀態機本身的行為才是這裡要釘住的東西。
 */
const GROUPS: readonly SupportFaqGroupLike[] = [
  {
    title: '訂購與付款',
    items: [
      { question: '什麼時候可以下單？', answer: '開團期間都可以。' },
      { question: '可以怎麼付款？', answer: '綠界線上付款。' },
    ],
  },
  {
    title: '運送與取貨',
    items: [{ question: '運費怎麼算？', answer: '超商 60，宅配 120。' }],
  },
];

function dispatch(
  state: typeof initialSupportWidgetState,
  action: Parameters<typeof supportWidgetReducer>[1],
) {
  return supportWidgetReducer(state, action, GROUPS);
}

describe('收合／展開', () => {
  it('初始狀態是關閉的、view 是 root', () => {
    expect(initialSupportWidgetState.open).toBe(false);
    expect(initialSupportWidgetState.view).toBe('root');
  });

  it('open 之後 open=true、view 回到 root（每次重新打開都從頭開始）', () => {
    const opened = dispatch(initialSupportWidgetState, { type: 'open' });
    expect(opened.open).toBe(true);
    expect(opened.view).toBe('root');
  });

  it('close 之後整個狀態重置（不留上次填的內容）', () => {
    const dirty = { ...initialSupportWidgetState, open: true, message: '哈囉', view: 'contactForm' as const };
    const closed = dispatch(dirty, { type: 'close' });
    expect(closed.open).toBe(false);
    expect(closed.message).toBe('');
  });
});

describe('選單走到答案', () => {
  it('selectGroup → view 變 group', () => {
    const opened = dispatch(initialSupportWidgetState, { type: 'open' });
    const grouped = dispatch(opened, { type: 'selectGroup', groupIndex: 0 });
    expect(grouped.view).toBe('group');
    expect(grouped.groupIndex).toBe(0);
  });

  it('selectItem → view 變 answer', () => {
    const state = [
      { type: 'open' as const },
      { type: 'selectGroup' as const, groupIndex: 0 },
      { type: 'selectItem' as const, itemIndex: 1 },
    ].reduce(dispatch, initialSupportWidgetState);
    expect(state.view).toBe('answer');
    expect(state.itemIndex).toBe(1);
  });

  it('不存在的 groupIndex／itemIndex 不會讓狀態機爛掉，原地不動', () => {
    const opened = dispatch(initialSupportWidgetState, { type: 'open' });
    expect(dispatch(opened, { type: 'selectGroup', groupIndex: 99 })).toEqual(opened);

    const grouped = dispatch(opened, { type: 'selectGroup', groupIndex: 0 });
    expect(dispatch(grouped, { type: 'selectItem', itemIndex: 99 })).toEqual(grouped);
  });
});

describe('走到「其他問題」進留言表單', () => {
  it('root 直接選「其他問題」→ contactForm，menuPath 只有這一格', () => {
    const opened = dispatch(initialSupportWidgetState, { type: 'open' });
    const form = dispatch(opened, { type: 'startOtherQuestion' });
    expect(form.view).toBe('contactForm');
    expect(form.menuPath).toEqual([OTHER_QUESTION_MENU_PATH_ENTRY]);
  });
});

describe('答案頁「這樣有解決嗎？」', () => {
  const answered = [
    { type: 'open' as const },
    { type: 'selectGroup' as const, groupIndex: 1 },
    { type: 'selectItem' as const, itemIndex: 0 },
  ].reduce(dispatch, initialSupportWidgetState);

  it('有解決 → 直接關閉（不留言）', () => {
    const result = dispatch(answered, { type: 'markResolved' });
    expect(result.open).toBe(false);
  });

  it('沒有解決 → 進 contactForm，menuPath 真的有帶出去（不是空陣列），內容是走過的題目', () => {
    const result = dispatch(answered, { type: 'markUnresolved' });
    expect(result.view).toBe('contactForm');
    expect(result.menuPath.length).toBeGreaterThan(0);
    expect(result.menuPath).toEqual(['運送與取貨', '運費怎麼算？']);
  });
});

describe('back 導覽', () => {
  it('group → root', () => {
    const grouped = dispatch(dispatch(initialSupportWidgetState, { type: 'open' }), {
      type: 'selectGroup',
      groupIndex: 0,
    });
    expect(dispatch(grouped, { type: 'back' }).view).toBe('root');
  });

  it('answer → group', () => {
    const answered = [
      { type: 'open' as const },
      { type: 'selectGroup' as const, groupIndex: 0 },
      { type: 'selectItem' as const, itemIndex: 0 },
    ].reduce(dispatch, initialSupportWidgetState);
    expect(dispatch(answered, { type: 'back' }).view).toBe('group');
  });

  it('從題目走進來的 contactForm → back 回 answer', () => {
    const form = [
      { type: 'open' as const },
      { type: 'selectGroup' as const, groupIndex: 0 },
      { type: 'selectItem' as const, itemIndex: 0 },
      { type: 'markUnresolved' as const },
    ].reduce(dispatch, initialSupportWidgetState);
    expect(dispatch(form, { type: 'back' }).view).toBe('answer');
  });

  it('從「其他問題」走進來的 contactForm → back 回 root', () => {
    const form = dispatch(dispatch(initialSupportWidgetState, { type: 'open' }), {
      type: 'startOtherQuestion',
    });
    expect(dispatch(form, { type: 'back' }).view).toBe('root');
  });
});

describe('留言表單欄位與送出', () => {
  const form = dispatch(dispatch(initialSupportWidgetState, { type: 'open' }), {
    type: 'startOtherQuestion',
  });

  it('setMessage／setContactEmail／setContactPhone 各自更新對應欄位', () => {
    let next = dispatch(form, { type: 'setMessage', value: '哈囉' });
    next = dispatch(next, { type: 'setContactEmail', value: 'a@b.com' });
    next = dispatch(next, { type: 'setContactPhone', value: '0912345678' });
    expect(next.message).toBe('哈囉');
    expect(next.contactEmail).toBe('a@b.com');
    expect(next.contactPhone).toBe('0912345678');
  });

  it('prefillContactEmail：客人還沒填過才生效', () => {
    const prefilled = dispatch(form, { type: 'prefillContactEmail', value: 'me@greygray.tw' });
    expect(prefilled.contactEmail).toBe('me@greygray.tw');
  });

  it('prefillContactEmail：客人已經填過就不覆蓋（不能蓋掉客人自己改的內容）', () => {
    const typed = dispatch(form, { type: 'setContactEmail', value: 'typed@b.com' });
    const prefilled = dispatch(typed, { type: 'prefillContactEmail', value: 'me@greygray.tw' });
    expect(prefilled.contactEmail).toBe('typed@b.com');
  });

  it('submitStart → submitting=true、清掉舊的錯誤訊息', () => {
    const errored = dispatch(form, { type: 'submitError', message: '舊錯誤' });
    const submitting = dispatch(errored, { type: 'submitStart' });
    expect(submitting.submitting).toBe(true);
    expect(submitting.errorMessage).toBeNull();
  });

  it('submitSuccess → view 變 success，送出後要告訴客人多久會回（由畫面層讀 view==="success" 決定要顯示什麼）', () => {
    const submitting = dispatch(form, { type: 'submitStart' });
    const done = dispatch(submitting, { type: 'submitSuccess', ticketId: 'ticket-1' });
    expect(done.view).toBe('success');
    expect(done.ticketId).toBe('ticket-1');
    expect(done.submitting).toBe(false);
  });

  it('submitError → 429 顯示的是看得懂的訊息，且回到可以重送的狀態', () => {
    const submitting = dispatch(form, { type: 'submitStart' });
    const errored = dispatch(submitting, { type: 'submitError', message: '你剛剛已經留過言了，我們會儘快回覆。' });
    expect(errored.submitting).toBe(false);
    expect(errored.view).toBe('contactForm');
    expect(errored.errorMessage).toBe('你剛剛已經留過言了，我們會儘快回覆。');
  });
});
