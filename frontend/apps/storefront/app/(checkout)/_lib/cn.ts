/**
 * 極簡 classnames 合併。`@greygray/ui` 內部也有一份一樣的東西，
 * 但那份沒有從套件對外匯出（見 `packages/ui/src/components/internal/cn.ts`），
 * 這個套件也不能加 `clsx` 依賴，所以這裡自己放一份，不依賴別人的內部檔案。
 */
export type ClassValue = string | number | null | undefined | false | ClassValue[];

export function cn(...inputs: ClassValue[]): string {
  const out: string[] = [];
  for (const input of inputs) {
    if (!input) continue;
    if (Array.isArray(input)) {
      const nested = cn(...input);
      if (nested) out.push(nested);
    } else {
      out.push(String(input));
    }
  }
  return out.join(' ');
}
