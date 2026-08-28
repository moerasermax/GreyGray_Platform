import { cn } from './internal/cn';

export interface BottomActionBarProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode;
}

/** 常駐底部加購列。sticky 定位，處理 iPhone 的 safe-area-inset-bottom。 */
export function BottomActionBar({ children, className, ...rest }: BottomActionBarProps) {
  return (
    <div
      className={cn(
        'fixed inset-x-0 bottom-0 z-[var(--gg-z-bottom-bar)] flex items-center',
        'gap-[var(--gg-space-3)] border-t border-border-soft bg-surface',
        'px-[var(--gg-space-4)] shadow-bottom-bar',
        className,
      )}
      style={{
        minHeight: 'var(--gg-bottom-bar-height)',
        paddingTop: 'var(--gg-space-3)',
        paddingBottom: 'calc(var(--gg-space-3) + env(safe-area-inset-bottom, 0px))',
      }}
      {...rest}
    >
      {children}
    </div>
  );
}
