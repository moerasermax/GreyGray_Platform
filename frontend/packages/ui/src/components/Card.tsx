import { cn } from './internal/cn';

export type CardPadding = 'none' | 'sm' | 'md' | 'lg';

const PADDING_CLASS: Record<CardPadding, string> = {
  none: '',
  sm: 'p-[var(--gg-space-3)]',
  md: 'p-[var(--gg-space-5)]',
  lg: 'p-[var(--gg-space-6)]',
};

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  padding?: CardPadding;
  /** 陰影是否要更明顯（例如浮在內容上方的卡片）。預設用一般卡片陰影。 */
  raised?: boolean;
}

/** 圓角 16px、柔陰影無硬邊——沒有 border，靠陰影分層。 */
export function Card({ padding = 'md', raised = false, className, children, ...rest }: CardProps) {
  return (
    <div
      className={cn(
        'rounded-card bg-surface',
        raised ? 'shadow-raised' : 'shadow-card',
        PADDING_CLASS[padding],
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  );
}
