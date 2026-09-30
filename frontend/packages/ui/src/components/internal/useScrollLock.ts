import { useEffect } from 'react';

export interface ScrollLockTarget {
  style: {
    overflow: string;
  };
}

/** 鎖定抽屜背後的根捲動容器，並回傳原值供關閉時精確還原。 */
export function navDrawerLockScroll(target: ScrollLockTarget): string {
  const previousOverflow = target.style.overflow;
  target.style.overflow = 'hidden';
  return previousOverflow;
}

/** 還原鎖定前的值；不能一律清成空字串，呼叫端可能原本就有設定。 */
export function navDrawerRestoreScroll(
  target: ScrollLockTarget,
  previousOverflow: string,
): void {
  target.style.overflow = previousOverflow;
}

/** NavDrawer 專用：只鎖 html，不把 body 變成新的捲動容器。 */
export function useScrollLock(open: boolean): void {
  useEffect(() => {
    if (!open) return;

    const root = document.documentElement;
    const previousOverflow = navDrawerLockScroll(root);
    return () => navDrawerRestoreScroll(root, previousOverflow);
  }, [open]);
}
