import { cn } from './internal/cn';

export interface TopBarProps {
  /** 左邊的插槽。通常是返回連結。 */
  left?: React.ReactNode;
  /** 中間的標題文字。單行、超出截斷——商品名可以很長。 */
  title: string;
  /** 右邊的插槽。商品詳情放帶徽章的購物車連結，其餘頁面留空。 */
  right?: React.ReactNode;
  className?: string;
}

/**
 * 頁面自己的頂部列：左（返回）· 中（頁名）· 右（選配）。
 *
 * ── 為什麼是 `sticky` 而不是 `fixed` ──
 * `fixed` 會脫離文件流，於是每一頁都得再補一段頂部留白，那正是
 * `globals.css` 給 `<body>` 補底部留白之後留下的那種難解關係（FE-23 的 144px 教訓）。
 * `sticky` 仍佔位置，內容自然往下排，**不需要任何一處補留白**。
 *
 * ── 尺寸與層級一律從 token 來 ──
 * 高度 `--gg-top-bar-height`、層級 `--gg-z-header`（token 早就定義了但一直沒人用，
 * 它就是為這種東西準備的：比 `--gg-z-bottom-bar` 高、比 `--gg-z-modal` 低）。
 * 上方 `env(safe-area-inset-top)` 對應 `BottomActionBar` 處理 bottom inset 的做法——
 * 瀏海機橫放或站在狀態列底下時，內容不會被切掉。
 *
 * **這個元件不碰 router。** 「返回要去哪」是 app 的事，由呼叫端塞進 `left`。
 */
export function TopBar({ left, title, right, className }: TopBarProps) {
  return (
    <header
      className={cn(
        'sticky top-0 z-[var(--gg-z-header)] flex items-center gap-[var(--gg-space-2)]',
        'border-b border-border-soft bg-surface px-[var(--gg-space-3)]',
        className,
      )}
      style={{
        minHeight: 'calc(var(--gg-top-bar-height) + env(safe-area-inset-top, 0px))',
        paddingTop: 'env(safe-area-inset-top, 0px)',
      }}
    >
      {/*
        左右兩個插槽都給固定的最小寬度，標題才會真的置中——
        只有左邊有東西時，`flex-1` 的標題會被推向右邊，看起來像沒對齊。
      */}
      <div className="flex min-w-[var(--gg-touch-min)] shrink-0 items-center justify-start">
        {left}
      </div>
      {/*
        `min-w-0` 是 `truncate` 在 flex 子項裡生效的前提（預設 min-width:auto 不會縮）。
        這裡刻意不是 <h1>：每一頁都已經有自己的 <h1>，頂部列只是重複那個標題，
        再宣告一次標題層級會讓螢幕閱讀器的大綱多出一層假的結構。
      */}
      <p className="min-w-0 flex-1 truncate text-center font-display text-[length:var(--gg-text-base)] font-bold text-fg">
        {title}
      </p>
      <div className="flex min-w-[var(--gg-touch-min)] shrink-0 items-center justify-end">
        {right}
      </div>
    </header>
  );
}
