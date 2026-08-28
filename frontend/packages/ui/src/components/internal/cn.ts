/** 極簡 classnames 合併，不引入 clsx——這個套件不能動 package.json（見 FE-2 交付說明）。 */
export type ClassValue =
  | string
  | number
  | null
  | undefined
  | false
  | ClassValue[]
  | Record<string, boolean | null | undefined>;

export function cn(...inputs: ClassValue[]): string {
  const out: string[] = [];
  for (const input of inputs) {
    if (!input) continue;
    if (typeof input === 'string' || typeof input === 'number') {
      out.push(String(input));
    } else if (Array.isArray(input)) {
      const nested = cn(...input);
      if (nested) out.push(nested);
    } else {
      for (const key in input) {
        if (input[key]) out.push(key);
      }
    }
  }
  return out.join(' ');
}
