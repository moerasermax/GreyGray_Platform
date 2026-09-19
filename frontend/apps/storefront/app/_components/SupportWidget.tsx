'use client';

/*
 * 右下角客服小幫手（ADR-040）。全站殼，掛在根 `layout.tsx`——理由跟 `StorefrontTabBar`
 * 一樣：前台沒有共同的中介 layout，這裡是唯一「一處解決全部」的位置。
 *
 * 選項與答案**直接 import `FAQ_GROUPS`**（不另抄一份），不接 AI、不接第三方客服套件
 * （ADR-040 明確否決）。狀態機在 `_lib/supportWidget.ts`、畫面在 `SupportWidgetPanel.tsx`，
 * 這支只負責：收合按鈕、掛 `BottomSheet`、接 API、把 dispatch 包成 callback 傳給純元件。
 *
 * ── 收合按鈕的位置為什麼是這個算式 ──
 * 全站任何一頁最多只有一條固定在底部的列：`StorefrontTabBar`（`_lib/tabs.ts` 黑名單控制
 * 哪幾頁隱藏）或 `BottomActionBar`（商品頁／購物車／結帳頁），兩者的高度**都是同一個
 * token** `--gg-bottom-bar-height`。收合按鈕的 `bottom` 疊上同一個 token 再加一段間距，
 * 保證它永遠貼在那條列的正上方、不會疊在一起，也不會蓋住結帳頁的送出鈕或購物車的結帳鈕——
 * 這條規則不必逐頁枚舉，只要兩邊繼續共用同一個 token 就成立。
 * 360px 下 `right`／`bottom` 都用 token 間距，不會貼死螢幕邊緣。
 */
import { getMe } from '@greygray/api-client/endpoints/storefront';
import { BottomSheet, IconButton, IconMessageCircle, IconX } from '@greygray/ui';
import { useEffect, useReducer, useState } from 'react';
import { FAQ_GROUPS } from '../(info)/_content/faq';
import { browserApi } from '../_lib/apiClient';
import { createSupportTicket } from '../_lib/supportTickets';
import { supportSubmitErrorMessage } from '../_lib/supportErrors';
import { canSubmitSupportTicket } from '../_lib/supportValidation';
import { initialSupportWidgetState, supportWidgetReducer } from '../_lib/supportWidget';
import { SupportWidgetPanel } from './SupportWidgetPanel';

export function SupportWidget() {
  const [state, dispatch] = useReducer(
    (current: typeof initialSupportWidgetState, action: Parameters<typeof supportWidgetReducer>[1]) =>
      supportWidgetReducer(current, action, FAQ_GROUPS),
    initialSupportWidgetState,
  );
  const [prefillAttempted, setPrefillAttempted] = useState(false);

  // 已登入客人的 email 可以預填（`Me.email` 是明文）；手機契約上只回遮罩過的
  // `phoneNumberMasked`，把遮罩字串當聯絡方式送出去會變成一組打不通的假手機，
  // 所以手機不預填，讓客人自己填——見 §5「停下來回報」判斷後的做法，寫進報告。
  useEffect(() => {
    if (!state.open || state.view !== 'contactForm' || prefillAttempted) return;
    setPrefillAttempted(true);
    getMe(browserApi())
      .then((me) => {
        if (me.email) dispatch({ type: 'prefillContactEmail', value: me.email });
      })
      .catch(() => {
        // 訪客沒有登入，401 是預期行為——留言表單本來就要能匿名用，不擋。
      });
  }, [state.open, state.view, prefillAttempted]);

  async function handleSubmit() {
    if (!canSubmitSupportTicket(state.message, state.contactEmail, state.contactPhone)) return;
    dispatch({ type: 'submitStart' });
    try {
      const ticket = await createSupportTicket(browserApi(), {
        message: state.message.trim(),
        contactEmail: state.contactEmail.trim() || null,
        contactPhone: state.contactPhone.trim() || null,
        menuPath: state.menuPath,
      });
      dispatch({ type: 'submitSuccess', ticketId: ticket.id });
    } catch (cause) {
      dispatch({ type: 'submitError', message: supportSubmitErrorMessage(cause) });
    }
  }

  return (
    <>
      <IconButton
        icon={state.open ? <IconX /> : <IconMessageCircle />}
        aria-label={state.open ? '關閉客服小幫手' : '打開客服小幫手'}
        variant="primary"
        size="lg"
        onClick={() => dispatch({ type: state.open ? 'close' : 'open' })}
        className="fixed right-[var(--gg-space-4)] z-[var(--gg-z-header)] shadow-raised"
        style={{
          bottom:
            'calc(var(--gg-bottom-bar-height) + env(safe-area-inset-bottom, 0px) + var(--gg-space-3))',
        }}
      />

      <BottomSheet open={state.open} onClose={() => dispatch({ type: 'close' })} title="客服小幫手">
        <SupportWidgetPanel
          state={state}
          groups={FAQ_GROUPS}
          onSelectGroup={(groupIndex) => dispatch({ type: 'selectGroup', groupIndex })}
          onSelectItem={(itemIndex) => dispatch({ type: 'selectItem', itemIndex })}
          onBack={() => dispatch({ type: 'back' })}
          onMarkResolved={() => dispatch({ type: 'markResolved' })}
          onMarkUnresolved={() => dispatch({ type: 'markUnresolved' })}
          onStartOtherQuestion={() => dispatch({ type: 'startOtherQuestion' })}
          onMessageChange={(value) => dispatch({ type: 'setMessage', value })}
          onContactEmailChange={(value) => dispatch({ type: 'setContactEmail', value })}
          onContactPhoneChange={(value) => dispatch({ type: 'setContactPhone', value })}
          onSubmit={() => void handleSubmit()}
          onDone={() => dispatch({ type: 'close' })}
        />
      </BottomSheet>
    </>
  );
}
