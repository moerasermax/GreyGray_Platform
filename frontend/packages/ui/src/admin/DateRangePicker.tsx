import { Field, Input } from './Field';

export interface DateRange {
  readonly from: string | null;
  readonly to: string | null;
}

export interface DateRangePickerProps {
  readonly idPrefix: string;
  readonly label?: string;
  readonly value: DateRange;
  readonly onChange: (value: DateRange) => void;
}

/**
 * 兩個原生 `<input type="date">`，不引入日曆元件套件——
 * M1a 的篩選需求就是「從哪天到哪天」，原生控制項就夠用。
 */
export function DateRangePicker({ idPrefix, label = '日期範圍', value, onChange }: DateRangePickerProps) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-sm font-medium text-fg">{label}</span>
      <div className="flex items-center gap-2">
        <Field label="起" htmlFor={`${idPrefix}-from`}>
          <Input
            id={`${idPrefix}-from`}
            type="date"
            value={value.from ?? ''}
            max={value.to ?? undefined}
            onChange={(event) => onChange({ ...value, from: event.target.value || null })}
          />
        </Field>
        <span className="mt-5 text-fg-muted">—</span>
        <Field label="迄" htmlFor={`${idPrefix}-to`}>
          <Input
            id={`${idPrefix}-to`}
            type="date"
            value={value.to ?? ''}
            min={value.from ?? undefined}
            onChange={(event) => onChange({ ...value, to: event.target.value || null })}
          />
        </Field>
      </div>
    </div>
  );
}
