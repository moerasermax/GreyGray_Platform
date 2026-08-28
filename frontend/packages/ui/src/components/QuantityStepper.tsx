import { cn } from './internal/cn';
import { IconButton } from './IconButton';
import { IconMinus, IconPlus } from './icons';

export interface QuantityStepperProps {
  value: number;
  min?: number;
  max?: number;
  step?: number;
  onChange: (value: number) => void;
  disabled?: boolean;
  'aria-label'?: string;
  className?: string;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

/** 完全受控，加減鈕各 44×44（IconButton 已保證），中間可直接輸入。 */
export function QuantityStepper({
  value,
  min = 1,
  max = Number.MAX_SAFE_INTEGER,
  step = 1,
  onChange,
  disabled = false,
  className,
  ...rest
}: QuantityStepperProps) {
  const ariaLabel = rest['aria-label'] ?? '數量';

  function handleInputChange(event: React.ChangeEvent<HTMLInputElement>) {
    const parsed = Number.parseInt(event.target.value, 10);
    if (Number.isNaN(parsed)) return;
    onChange(clamp(parsed, min, max));
  }

  return (
    <div
      className={cn(
        'inline-flex items-center gap-[var(--gg-space-2)] rounded-pill border border-border-soft bg-surface p-[var(--gg-space-1)]',
        className,
      )}
    >
      <IconButton
        icon={<IconMinus />}
        aria-label={`減少${ariaLabel}`}
        size="sm"
        disabled={disabled || value <= min}
        onClick={() => onChange(clamp(value - step, min, max))}
      />
      <input
        type="text"
        inputMode="numeric"
        aria-label={ariaLabel}
        value={value}
        disabled={disabled}
        onChange={handleInputChange}
        className="w-[var(--gg-space-7)] bg-transparent text-center font-display text-[length:var(--gg-text-base)] font-bold text-fg outline-none disabled:opacity-50"
      />
      <IconButton
        icon={<IconPlus />}
        aria-label={`增加${ariaLabel}`}
        size="sm"
        disabled={disabled || value >= max}
        onClick={() => onChange(clamp(value + step, min, max))}
      />
    </div>
  );
}
