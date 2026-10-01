import { Dialog, Field, Input, Textarea } from '@greygray/ui/admin';

export interface ManualRefundDialogProps {
  readonly open: boolean;
  readonly amountInput: string;
  readonly remittedOn: string;
  readonly noteInput: string;
  readonly today: string;
  readonly error: string | null;
  readonly submitting: boolean;
  readonly onAmountChange: (value: string) => void;
  readonly onRemittedOnChange: (value: string) => void;
  readonly onNoteChange: (value: string) => void;
  readonly onClose: () => void;
  readonly onSubmit: () => void;
}

export function ManualRefundDialog(props: ManualRefundDialogProps) {
  return (
    <Dialog
      open={props.open}
      onClose={props.onClose}
      title="登記已匯款"
      description="只登記已實際匯出的款項；可分次登記，送出後不可修改或刪除。"
      footer={
        <>
          <button type="button" onClick={props.onClose} disabled={props.submitting} className="rounded-full border border-border-strong px-4 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60">取消</button>
          <button type="button" onClick={props.onSubmit} disabled={props.submitting} className="rounded-full border border-primary bg-primary px-4 py-1.5 text-sm font-semibold text-on-primary hover:opacity-90 disabled:opacity-60">{props.submitting ? '登記中…' : '確認登記'}</button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label="匯款金額（整數元）" htmlFor="manual-refund-amount" required>
          <Input id="manual-refund-amount" inputMode="numeric" value={props.amountInput} onChange={(event) => props.onAmountChange(event.target.value)} placeholder="例如 500" />
        </Field>
        <Field label="匯款日期" htmlFor="manual-refund-date" required>
          <Input id="manual-refund-date" type="date" max={props.today} value={props.remittedOn} onChange={(event) => props.onRemittedOnChange(event.target.value)} />
        </Field>
        <Field label="備註" htmlFor="manual-refund-note">
          <Textarea id="manual-refund-note" rows={3} maxLength={200} value={props.noteInput} onChange={(event) => props.onNoteChange(event.target.value)} placeholder="例如：匯款帳號後五碼" />
        </Field>
        <p className="text-sm font-medium text-danger">請勿填寫客人完整銀行帳號。</p>
        {props.error ? <p role="alert" className="text-sm text-danger">{props.error}</p> : null}
      </div>
    </Dialog>
  );
}
