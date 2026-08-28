/**
 * mock fixture 的 `imageUrl` 全部是 `null`（真實圖片走 Cloudflare R2，M1a 還沒有）。
 * `ProductCard`／`CategoryChip` 的 `imageSrc` 是必填字串，這裡補一個 data URI 佔位圖，
 * 純粹是排版用的視覺佔位，不是「猜」出來的商品圖。
 */
export function placeholderImage(label: string): string {
  const safeLabel = label
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .slice(0, 8);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="400">
    <rect width="400" height="400" fill="transparent" />
    <text x="200" y="212" font-family="sans-serif" font-size="28" fill="currentColor" text-anchor="middle">${safeLabel}</text>
  </svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}
