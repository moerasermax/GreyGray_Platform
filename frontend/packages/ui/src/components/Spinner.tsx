import { cn } from './internal/cn';

export interface SpinnerProps extends React.SVGAttributes<SVGSVGElement> {
  /** 給獨立使用時的無障礙標籤；當 Spinner 只是按鈕裡的裝飾時留空即可（按鈕本身已有文字）。 */
  label?: string;
}

export function Spinner({ label, className, ...rest }: SpinnerProps) {
  return (
    <svg
      width="1em"
      height="1em"
      viewBox="0 0 24 24"
      fill="none"
      className={cn('animate-spin', className)}
      role={label ? 'status' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      {...rest}
    >
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth={2.5} opacity={0.25} />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        strokeWidth={2.5}
        strokeLinecap="round"
      />
    </svg>
  );
}
