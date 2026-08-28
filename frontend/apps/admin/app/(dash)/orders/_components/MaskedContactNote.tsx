import { MaskIcon } from '@greygray/ui/admin';

export interface MaskedContactNoteProps {
  readonly maskedContact: string | null | undefined;
}

/**
 * 客戶聯絡方式一律先看遮罩版。
 *
 * 契約（`docs/api/openapi.admin.yaml`）目前只有 `customerContactMasked`，
 * **沒有解遮罩端點**——查看明文須另外操作並填寫存取理由、每次都寫入 audit 的規則
 * 要等契約補上對應端點才能真正實作，這裡先把「看得到會被記錄」的提醒做出來，
 * 不要在前端自己補一個假的解遮罩按鈕（見交付回報的契約問題）。
 */
export function MaskedContactNote({ maskedContact }: MaskedContactNoteProps) {
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <MaskIcon className="h-4 w-4 text-fg-muted" />
        <span className="gg-numeric text-sm text-fg">{maskedContact ?? '—'}</span>
      </div>
      <p className="text-xs text-fg-muted">
        聯絡方式預設遮罩，<span className="font-medium text-fg">查看明文會被記錄</span>——
        須另外操作並填寫存取理由，每一次查看都會寫入稽核紀錄。
      </p>
    </div>
  );
}
