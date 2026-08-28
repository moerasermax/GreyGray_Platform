'use client';

import { useEffect, useRef } from 'react';

/**
 * 捲到底部觸發載入下一頁的共用 hook。
 * `enabled` 為 false（通常是 `nextCursor === null`）時完全不掛 observer，
 * 確保最後一頁之後不會再打 API。
 */
export function useInfiniteScrollSentinel(onIntersect: () => void, enabled: boolean) {
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  const onIntersectRef = useRef(onIntersect);
  onIntersectRef.current = onIntersect;

  useEffect(() => {
    if (!enabled) return;
    const node = sentinelRef.current;
    if (!node) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting) onIntersectRef.current();
      },
      { rootMargin: '480px' },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [enabled]);

  return sentinelRef;
}
