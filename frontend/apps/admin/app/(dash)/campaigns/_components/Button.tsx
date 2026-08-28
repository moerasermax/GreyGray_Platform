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

/** 後台元件庫沒有 Button（那是前台 FE-2 的地盤），這裡是頁面自己的最小按鈕樣式。 */
export function Button({ variant = 'secondary', className, type = 'button', ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={`rounded-full px-4 py-1.5 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-60 ${VARIANT_CLASS[variant]} ${className ?? ''}`.trim()}
      {...rest}
    />
  );
}
