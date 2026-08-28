/**
 * 契約要求 `Id` 是 32 字元十六進位、無連字號（UUIDv7 的線上形狀，見 `docs/05-API契約.md` §6）。
 * mock 不需要真的時間可排序，只需要**固定不變、格式合法**，這樣 fixture 之間互相參照
 * （例如購物車 line 的 `skuId` 要對得上商品 fixture 的 SKU id）才能寫死在程式碼裡讀。
 */
export function hexId(seed: string): string {
  let h1 = 0xdeadbeef ^ seed.length;
  let h2 = 0x41c6ce57 ^ seed.length;
  for (let i = 0; i < seed.length; i += 1) {
    const ch = seed.charCodeAt(i);
    h1 = Math.imul(h1 ^ ch, 2654435761);
    h2 = Math.imul(h2 ^ ch, 1597334677);
  }
  h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507) ^ Math.imul(h2 ^ (h2 >>> 13), 3266489909);
  h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507) ^ Math.imul(h1 ^ (h1 >>> 13), 3266489909);
  const a = (h1 >>> 0).toString(16).padStart(8, '0');
  const b = (h2 >>> 0).toString(16).padStart(8, '0');
  return `${a}${b}${a}${b}`.slice(0, 32);
}
