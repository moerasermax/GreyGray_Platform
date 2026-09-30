import type {
  InputHTMLAttributes,
  ReactNode,
  Ref,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react';
import { useId } from 'react';

export interface FieldProps {
  readonly label: string;
  readonly htmlFor: string;
  readonly error?: string | null | undefined;
  readonly hint?: string;
  readonly required?: boolean;
  readonly children: ReactNode;
}

/** 標籤一律可見，不靠 placeholder 頂替；錯誤訊息貼在欄位下方。 */
export function Field({ label, htmlFor, error, hint, required, children }: FieldProps) {
  const hintId = `${htmlFor}-hint`;
  const errorId = `${htmlFor}-error`;

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={htmlFor} className="text-sm font-medium text-fg">
        {label}
        {required ? <span className="ml-0.5 text-danger">*</span> : null}
      </label>
      {children}
      {hint && !error ? (
        <p id={hintId} className="text-xs text-fg-muted">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="text-xs font-medium text-danger">
          {error}
        </p>
      ) : null}
    </div>
  );
}

const baseControlClass =
  'rounded-sm border border-border-strong bg-surface px-3 py-1.5 text-sm text-fg placeholder:text-fg-muted focus-visible:outline-2 focus-visible:outline-primary-strong disabled:cursor-not-allowed disabled:opacity-60';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  readonly invalid?: boolean;
  readonly ref?: Ref<HTMLInputElement>;
}

export function Input({ className, invalid, ...props }: InputProps) {
  return (
    <input
      className={`${baseControlClass} ${invalid ? 'border-danger' : ''} ${className ?? ''}`.trim()}
      aria-invalid={invalid}
      {...props}
    />
  );
}

export interface SelectOption {
  readonly value: string;
  readonly label: string;
}

export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'children'> {
  readonly options: readonly SelectOption[];
  readonly placeholder?: string;
}

export function Select({ className, options, placeholder, ...props }: SelectProps) {
  return (
    <select className={`${baseControlClass} ${className ?? ''}`.trim()} {...props}>
      {placeholder ? <option value="">{placeholder}</option> : null}
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

export type TextareaProps = TextareaHTMLAttributes<HTMLTextAreaElement>;

export function Textarea({ className, ...props }: TextareaProps) {
  return <textarea className={`${baseControlClass} ${className ?? ''}`.trim()} {...props} />;
}

/** 需要 htmlFor 又懶得自己想 id 時用這個。 */
export function useFieldId(prefix: string): string {
  const id = useId();
  return `${prefix}-${id}`;
}
