/**
 * 客服小幫手 BottomSheet 裡面的內容。**純呈現**——畫什麼完全由 `state` 決定，
 * 不自己呼叫 API、不自己管理狀態，狀態機在 `_lib/supportWidget.ts`。
 *
 * 拆成純元件是為了測得到：`SupportWidget.tsx` 掛了 `BottomSheet`（`createPortal`
 * 依賴 `document`），`renderToStaticMarkup` 在 SSR 下量不到 portal 的內容
 * （`(checkout)/_components/DeliveryMethodPicker.tsx` 用 `BottomSheet` 也是同樣不直接測）。
 * 這一支不碰 portal，用 `priceDisplay.test.tsx` 那套 `renderToStaticMarkup` 就測得到全部四個畫面。
 */
import { Button, Field, Input, Textarea } from '@greygray/ui';
import {
  canSubmitSupportTicket,
  supportContactError,
  supportMessageLengthError,
  SUPPORT_MESSAGE_MAX_LENGTH,
} from '../_lib/supportValidation';
import { SUPPORT_WAIT_MESSAGE } from '../_lib/supportErrors';
import type { SupportFaqGroupLike, SupportWidgetState } from '../_lib/supportWidget';

export interface SupportWidgetPanelProps {
  readonly state: SupportWidgetState;
  readonly groups: readonly SupportFaqGroupLike[];
  readonly onSelectGroup: (groupIndex: number) => void;
  readonly onSelectItem: (itemIndex: number) => void;
  readonly onBack: () => void;
  readonly onMarkResolved: () => void;
  readonly onMarkUnresolved: () => void;
  readonly onStartOtherQuestion: () => void;
  readonly onMessageChange: (value: string) => void;
  readonly onContactEmailChange: (value: string) => void;
  readonly onContactPhoneChange: (value: string) => void;
  readonly onSubmit: () => void;
  readonly onDone: () => void;
}

const listButtonClass =
  'w-full rounded-[var(--gg-radius-sm)] border border-border-soft bg-surface px-[var(--gg-space-4)] ' +
  'py-[var(--gg-space-3)] text-left text-[length:var(--gg-text-sm)] font-medium text-fg ' +
  'transition-colors duration-[var(--gg-duration-fast)] ease-out-soft hover:bg-surface-sunken';

const backButtonClass =
  'text-[length:var(--gg-text-sm)] font-bold text-primary-text underline underline-offset-2';

export function SupportWidgetPanel({
  state,
  groups,
  onSelectGroup,
  onSelectItem,
  onBack,
  onMarkResolved,
  onMarkUnresolved,
  onStartOtherQuestion,
  onMessageChange,
  onContactEmailChange,
  onContactPhoneChange,
  onSubmit,
  onDone,
}: SupportWidgetPanelProps) {
  if (state.view === 'root') {
    return (
      <div className="flex flex-col gap-[var(--gg-space-2)]">
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          先看看常見問題有沒有答案，沒有的話可以留言給我們。
        </p>
        {groups.map((group, groupIndex) => (
          <button
            key={group.title}
            type="button"
            className={listButtonClass}
            onClick={() => onSelectGroup(groupIndex)}
          >
            {group.title}
          </button>
        ))}
        <button type="button" className={listButtonClass} onClick={onStartOtherQuestion}>
          其他問題
        </button>
      </div>
    );
  }

  if (state.view === 'group') {
    const group = state.groupIndex !== null ? groups[state.groupIndex] : undefined;
    return (
      <div className="flex flex-col gap-[var(--gg-space-2)]">
        <button type="button" className={backButtonClass} onClick={onBack}>
          ← 返回
        </button>
        {group?.items.map((item, itemIndex) => (
          <button
            key={item.question}
            type="button"
            className={listButtonClass}
            onClick={() => onSelectItem(itemIndex)}
          >
            {item.question}
          </button>
        ))}
      </div>
    );
  }

  if (state.view === 'answer') {
    const group = state.groupIndex !== null ? groups[state.groupIndex] : undefined;
    const item = group && state.itemIndex !== null ? group.items[state.itemIndex] : undefined;
    return (
      <div className="flex flex-col gap-[var(--gg-space-4)]">
        <button type="button" className={backButtonClass} onClick={onBack}>
          ← 返回
        </button>
        <div className="flex flex-col gap-[var(--gg-space-2)]">
          <p className="font-display text-[length:var(--gg-text-base)] font-bold text-fg">
            {item?.question}
          </p>
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{item?.answer}</p>
        </div>
        <div className="flex flex-col gap-[var(--gg-space-2)]">
          <p className="text-[length:var(--gg-text-sm)] font-medium text-fg">這樣有解決嗎？</p>
          <div className="flex gap-[var(--gg-space-3)]">
            <Button variant="secondary" onClick={onMarkResolved}>
              有解決
            </Button>
            <Button variant="primary" onClick={onMarkUnresolved}>
              沒有解決
            </Button>
          </div>
        </div>
      </div>
    );
  }

  if (state.view === 'contactForm') {
    const messageError = state.message.trim() !== '' ? supportMessageLengthError(state.message) : null;
    const contactHint = supportContactError(state.contactEmail, state.contactPhone) ?? 'email 與手機留一項就可以';
    const canSubmit = canSubmitSupportTicket(state.message, state.contactEmail, state.contactPhone) && !state.submitting;

    return (
      <div className="flex flex-col gap-[var(--gg-space-4)]">
        <button type="button" className={backButtonClass} onClick={onBack}>
          ← 返回
        </button>

        {state.errorMessage ? (
          <p role="alert" className="text-[length:var(--gg-text-sm)] font-medium text-danger">
            {state.errorMessage}
          </p>
        ) : null}

        <Field
          label="想告訴我們什麼？"
          htmlFor="support-widget-message"
          required
          error={messageError ?? undefined}
          hint={messageError ? undefined : `${state.message.trim().length} / ${SUPPORT_MESSAGE_MAX_LENGTH} 字`}
        >
          <Textarea
            id="support-widget-message"
            value={state.message}
            onChange={(event) => onMessageChange(event.target.value)}
            rows={4}
            placeholder="請描述您的問題，我們會盡快回覆。"
          />
        </Field>

        <Field label="Email" htmlFor="support-widget-email" hint={contactHint}>
          <Input
            id="support-widget-email"
            type="email"
            value={state.contactEmail}
            onChange={(event) => onContactEmailChange(event.target.value)}
            placeholder="you@example.com"
          />
        </Field>

        <Field label="手機" htmlFor="support-widget-phone">
          <Input
            id="support-widget-phone"
            type="tel"
            value={state.contactPhone}
            onChange={(event) => onContactPhoneChange(event.target.value)}
            placeholder="09xxxxxxxx"
          />
        </Field>

        <Button variant="primary" fullWidth loading={state.submitting} disabled={!canSubmit} onClick={onSubmit}>
          送出留言
        </Button>
      </div>
    );
  }

  // state.view === 'success'
  return (
    <div className="flex flex-col gap-[var(--gg-space-4)]">
      <p className="text-[length:var(--gg-text-base)] text-fg">{SUPPORT_WAIT_MESSAGE}</p>
      <Button variant="primary" fullWidth onClick={onDone}>
        知道了
      </Button>
    </div>
  );
}
