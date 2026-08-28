import type { components } from '@greygray/api-client/admin';
import { refundDestinationHint, refundDestinationLabel } from '../_lib/labels';

type S = components['schemas'];
type RefundDestination = S['RefundDestination'];

const OPTIONS: readonly RefundDestination[] = ['StoredValue', 'OriginalPaymentMethod'];

export interface RefundDestinationFieldsProps {
  readonly name: string;
  readonly value: RefundDestination;
  readonly onChange: (value: RefundDestination) => void;
}

/**
 * 取消退款去處的選項。**預設儲值金**（不動金流、零手續費），
 * 兩個選項的差異一定要寫在畫面上，不能只靠選項名稱讓人猜。
 */
export function RefundDestinationFields({ name, value, onChange }: RefundDestinationFieldsProps) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="text-sm font-medium text-fg">退款去處</legend>
      {OPTIONS.map((option) => {
        const inputId = `${name}-${option}`;
        const checked = value === option;
        return (
          <label
            key={option}
            htmlFor={inputId}
            className={`flex cursor-pointer flex-col gap-0.5 rounded-card border px-3 py-2 text-sm ${
              checked ? 'border-primary bg-primary-subtle' : 'border-border-soft bg-surface hover:bg-surface-sunken'
            }`}
          >
            <span className="flex items-center gap-2 font-medium text-fg">
              <input
                id={inputId}
                type="radio"
                name={name}
                value={option}
                checked={checked}
                onChange={() => onChange(option)}
                className="h-4 w-4 accent-[var(--color-primary)]"
              />
              {refundDestinationLabel(option)}
              {option === 'StoredValue' ? (
                <span className="rounded-full bg-success-subtle px-2 py-0.5 text-xs font-semibold text-success">
                  建議
                </span>
              ) : null}
            </span>
            <span className="pl-6 text-xs text-fg-muted">{refundDestinationHint(option)}</span>
          </label>
        );
      })}
    </fieldset>
  );
}
