import { cn } from './internal/cn';

export interface FieldProps {
  label: string;
  htmlFor: string;
  error?: string | undefined;
  hint?: string | undefined;
  required?: boolean;
  children: React.ReactNode;
  className?: string;
}

/** 標籤一律可見，不靠 placeholder 頂替。錯誤訊息貼在欄位下方。 */
export function Field({ label, htmlFor, error, hint, required, children, className }: FieldProps) {
  const hintId = hint ? `${htmlFor}-hint` : undefined;
  const errorId = error ? `${htmlFor}-error` : undefined;

  return (
    <div className={cn('flex flex-col gap-[var(--gg-space-1)]', className)}>
      <label
        htmlFor={htmlFor}
        className="text-[length:var(--gg-text-sm)] font-bold text-fg"
      >
        {label}
        {required && (
          <span aria-hidden className="ml-[var(--gg-space-1)] text-danger">
            *
          </span>
        )}
      </label>
      {children}
      {error ? (
        <p id={errorId} role="alert" className="text-[length:var(--gg-text-xs)] text-danger">
          {error}
        </p>
      ) : hint ? (
        <p id={hintId} className="text-[length:var(--gg-text-xs)] text-fg-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
