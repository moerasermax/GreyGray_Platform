/**
 * 客服小幫手的**選單狀態機**。抽成純函式（跟 `_lib/tabs.ts`、`_lib/cvsSelection.ts` 同一個理由）：
 * 這個 workspace 沒有 jsdom，互動狀態測不到渲染結果，只有先抽出來才測得到。
 *
 * 選項與答案不放在這裡——呼叫端傳 `FAQ_GROUPS`（`(info)/_content/faq.ts`）進來，
 * 這裡只認得 `{ title, items: { question, answer }[] }` 的形狀，不 import 那個檔，
 * 避免這支測試檔跟著 FE-36 改的文案一起變動。
 */

export interface SupportFaqItemLike {
  readonly question: string;
  readonly answer: string;
}

export interface SupportFaqGroupLike {
  readonly title: string;
  readonly items: readonly SupportFaqItemLike[];
}

export type SupportWidgetView = 'root' | 'group' | 'answer' | 'contactForm' | 'success';

export interface SupportWidgetState {
  readonly open: boolean;
  readonly view: SupportWidgetView;
  readonly groupIndex: number | null;
  readonly itemIndex: number | null;
  /** 客人走過的選單路徑，原樣送給後端（契約 `menuPath`，最多 5 層）。 */
  readonly menuPath: readonly string[];
  readonly message: string;
  readonly contactEmail: string;
  readonly contactPhone: string;
  readonly submitting: boolean;
  readonly errorMessage: string | null;
  readonly ticketId: string | null;
}

export const initialSupportWidgetState: SupportWidgetState = {
  open: false,
  view: 'root',
  groupIndex: null,
  itemIndex: null,
  menuPath: [],
  message: '',
  contactEmail: '',
  contactPhone: '',
  submitting: false,
  errorMessage: null,
  ticketId: null,
};

export type SupportWidgetAction =
  | { readonly type: 'open' }
  | { readonly type: 'close' }
  | { readonly type: 'selectGroup'; readonly groupIndex: number }
  | { readonly type: 'selectItem'; readonly itemIndex: number }
  | { readonly type: 'back' }
  | { readonly type: 'markResolved' }
  | { readonly type: 'markUnresolved' }
  | { readonly type: 'startOtherQuestion' }
  | { readonly type: 'setMessage'; readonly value: string }
  | { readonly type: 'setContactEmail'; readonly value: string }
  | { readonly type: 'setContactPhone'; readonly value: string }
  /** 已登入客人的 email 預填——只在客人自己還沒填過時才生效，不覆蓋客人已經改過的內容。 */
  | { readonly type: 'prefillContactEmail'; readonly value: string }
  | { readonly type: 'submitStart' }
  | { readonly type: 'submitSuccess'; readonly ticketId: string }
  | { readonly type: 'submitError'; readonly message: string };

/** `其他問題` 不對到任何一題，menuPath 就只有這一個標記。 */
export const OTHER_QUESTION_MENU_PATH_ENTRY = '其他問題';

export function supportWidgetReducer(
  state: SupportWidgetState,
  action: SupportWidgetAction,
  groups: readonly SupportFaqGroupLike[],
): SupportWidgetState {
  switch (action.type) {
    case 'open':
      return { ...initialSupportWidgetState, open: true };

    case 'close':
      return { ...initialSupportWidgetState, open: false };

    case 'selectGroup': {
      if (!groups[action.groupIndex]) return state;
      return { ...state, view: 'group', groupIndex: action.groupIndex, itemIndex: null, errorMessage: null };
    }

    case 'selectItem': {
      if (state.groupIndex === null) return state;
      const group = groups[state.groupIndex];
      if (!group?.items[action.itemIndex]) return state;
      return { ...state, view: 'answer', itemIndex: action.itemIndex, errorMessage: null };
    }

    case 'back': {
      if (state.view === 'group') return { ...state, view: 'root', groupIndex: null, errorMessage: null };
      if (state.view === 'answer') return { ...state, view: 'group', itemIndex: null, errorMessage: null };
      if (state.view === 'contactForm') {
        return {
          ...state,
          view: state.itemIndex !== null ? 'answer' : 'root',
          errorMessage: null,
        };
      }
      return state;
    }

    // 「這樣有解決嗎？」回答有 → 直接關掉，不留言。
    case 'markResolved':
      return { ...initialSupportWidgetState, open: false };

    // 回答沒有 → 進留言表單，menuPath 記下走過的題目。
    case 'markUnresolved': {
      const group = state.groupIndex !== null ? groups[state.groupIndex] : undefined;
      const item = group && state.itemIndex !== null ? group.items[state.itemIndex] : undefined;
      const menuPath = group && item ? [group.title, item.question] : [];
      return { ...state, view: 'contactForm', menuPath, errorMessage: null };
    }

    case 'startOtherQuestion':
      return {
        ...state,
        view: 'contactForm',
        groupIndex: null,
        itemIndex: null,
        menuPath: [OTHER_QUESTION_MENU_PATH_ENTRY],
        errorMessage: null,
      };

    case 'setMessage':
      return { ...state, message: action.value };

    case 'setContactEmail':
      return { ...state, contactEmail: action.value };

    case 'setContactPhone':
      return { ...state, contactPhone: action.value };

    case 'prefillContactEmail':
      return state.contactEmail.trim() !== '' ? state : { ...state, contactEmail: action.value };

    case 'submitStart':
      return { ...state, submitting: true, errorMessage: null };

    case 'submitSuccess':
      return { ...state, submitting: false, view: 'success', ticketId: action.ticketId, errorMessage: null };

    case 'submitError':
      return { ...state, submitting: false, errorMessage: action.message };

    default:
      return state;
  }
}
