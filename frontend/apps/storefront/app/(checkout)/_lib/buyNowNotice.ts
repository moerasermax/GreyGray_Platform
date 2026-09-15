/** `location.search` 裡只要有一個 `from=buy-now` 就顯示購物車確認提示。 */
export function shouldShowBuyNowNotice(search: string): boolean {
  const query = search.startsWith('?') ? search.slice(1) : search;
  try {
    return new URLSearchParams(query).getAll('from').includes('buy-now');
  } catch {
    return false;
  }
}
