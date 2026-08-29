import type { ButtonHTMLAttributes } from 'react';

type Variant = 'primary' | 'secondary' | 'danger';

const VARIANT_CLASS: Record<Variant, string> = {
  primary: 'bg-primary text-on-primary hover:bg-primary-hover',
  secondary: 'border border-border-strong text-fg hover:bg-surface-sunken',
  danger: 'border border-danger/30 bg-danger-subtle text-danger hover:opacity-90',
};

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  readonly variant?: Variant;
}

/**
 * 後台元件庫沒有 Button，這裡是頁面自己的最小按鈕樣式。
 * 現場的人站在店裡用手機單手操作，觸控目標維持最小 44×44（鐵則 6）。
 */
export function Button({ variant = 'secondary', className, type = 'button', ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={`min-h-11 min-w-11 rounded-full px-4 py-2 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-60 ${VARIANT_CLASS[variant]} ${className ?? ''}`.trim()}
      {...rest}
    />
  );
}
