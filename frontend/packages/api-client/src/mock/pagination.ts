/**
 * 游標分頁的 mock 版本。真正的游標是 UUIDv7 邊界，這裡用陣列索引的十進位字串代替——
 * 語意一樣（不透明、只能拿上一頁給的值），但不用真的產生時間排序的 ID。
 */

export interface Page<T> {
  readonly items: T[];
  readonly nextCursor: string | null;
}

export function paginate<T>(all: readonly T[], cursor: string | undefined | null, limit: number | undefined): Page<T> {
  const size = clampLimit(limit);
  const start = cursor ? Number.parseInt(cursor, 10) : 0;
  const offset = Number.isFinite(start) && start >= 0 ? start : 0;
  const items = all.slice(offset, offset + size);
  const nextOffset = offset + size;
  const nextCursor = nextOffset < all.length ? String(nextOffset) : null;
  return { items, nextCursor };
}

function clampLimit(limit: number | undefined): number {
  if (!limit || !Number.isFinite(limit)) return 20;
  return Math.min(Math.max(Math.trunc(limit), 1), 100);
}
