import type { components } from '@greygray/api-client/admin';
import { refundDestinationHint, refundDestinationLabel } from '../_lib/labels';

type S = components['schemas'];
type RefundDestination = S['RefundDestination'];

const OPTIONS: readonly RefundDestination[] = ['StoredValue', 'OriginalPaymentMethod'];

/** M1b 只開原路退款（ADR-023）。StoredValue 仍要顯示——契約與 ADR 都說這個選項存在，只是還沒開放。 */
const DISABLED_IN_M1B: ReadonlySet<RefundDestination> = new Set(['StoredValue']);

export interface RefundDestinationFieldsProps {
  readonly name: string;
  /** 不得有預設值——去向由客人選，`null` 代表還沒選。 */
  readonly value: RefundDestination | null;
  readonly onChange: (value: RefundDestination) => void;
}

/**
 * 退款去處由客人選，畫面上不能替客人預選（ADR-023）。
 * StoredValue 在 M1b 顯示但停用並標「M3 才開放」，不從選單移除。
 */
export function RefundDestinationFields({ name, value, onChange }: RefundDestinationFieldsProps) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="text-sm font-medium text-fg">退款去處（由客人選擇）</legend>
      {OPTIONS.map((option) => {
        const inputId = `${name}-${option}`;
        const checked = value === option;
        const disabled = DISABLED_IN_M1B.has(option);
        return (
          <label
            key={option}
            htmlFor={inputId}
            className={`flex flex-col gap-0.5 rounded-card border px-3 py-2 text-sm ${
              disabled
                ? 'cursor-not-allowed border-border-soft bg-surface-sunken opacity-60'
                : `cursor-pointer ${checked ? 'border-primary bg-primary-subtle' : 'border-border-soft bg-surface hover:bg-surface-sunken'}`
            }`}
          >
            <span className="flex items-center gap-2 font-medium text-fg">
              <input
                id={inputId}
                type="radio"
                name={name}
                value={option}
                checked={checked}
                disabled={disabled}
                required
                onChange={() => onChange(option)}
                className="h-4 w-4 accent-[var(--color-primary)]"
              />
              {refundDestinationLabel(option)}
              {disabled ? (
                <span className="rounded-full bg-surface px-2 py-0.5 text-xs font-semibold text-fg-muted">
                  M3 才開放
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
