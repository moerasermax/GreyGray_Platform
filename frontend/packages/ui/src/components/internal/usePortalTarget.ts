import { useEffect, useState } from 'react';

/**
 * 掛到 `document.body` 用的 portal 容器。
 *
 * SSR 沒有 `document`，所以第一次渲染一律回 `null`——
 * server 與 client 的首次輸出都是 `null`，不會有 hydration mismatch。
 * 掛載後才真的把內容搬到 body 底下。
 */
export function usePortalTarget(): HTMLElement | null {
  const [target, setTarget] = useState<HTMLElement | null>(null);
  useEffect(() => setTarget(document.body), []);
  return target;
}
