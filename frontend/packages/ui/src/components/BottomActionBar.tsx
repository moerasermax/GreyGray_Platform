import { cn } from './internal/cn';

export interface BottomActionBarProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode;
}

/**
 * 常駐底部加購列。fixed 定位（不是 sticky），處理 iPhone 的 safe-area-inset-bottom。
 *
 * 外層負責全寬的背景、上緣線與陰影；內層用 `--gg-container-max` 約束並置中，
 * 左右留白跟頁面的 `<main>` 同一組 token——寬螢幕上金額與按鈕才不會被拉到視窗兩端。
 * `children` 仍然是一個 flex 列的直接子項，`className`／`style`／其餘 attributes 都落在外層。
 */
export function BottomActionBar({ children, className, style, ...rest }: BottomActionBarProps) {
  return (
    <div
      className={cn(
        'fixed inset-x-0 bottom-0 z-[var(--gg-z-bottom-bar)] flex items-center',
        'border-t border-border-soft bg-surface shadow-bottom-bar',
        className,
      )}
      style={{
        minHeight: 'var(--gg-bottom-bar-height)',
        paddingTop: 'var(--gg-space-3)',
        paddingBottom: 'calc(var(--gg-space-3) + env(safe-area-inset-bottom, 0px))',
        ...style,
      }}
      {...rest}
    >
      <div className="mx-auto flex w-full min-w-0 max-w-[var(--gg-container-max)] items-center gap-[var(--gg-space-3)] px-[var(--gg-space-4)]">
        {children}
      </div>
    </div>
  );
}
