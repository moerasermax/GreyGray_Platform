/** `packages/ui` 的 `cn` 是內部檔案沒有對外匯出，這裡自己做一個最小版本，僅供本資料夾使用。 */
export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ');
}
