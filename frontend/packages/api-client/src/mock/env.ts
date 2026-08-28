/** `NEXT_PUBLIC_USE_MOCK=1` 時開啟 mock，後端好了之後**不改任何頁面程式碼**就能切回真的 BFF。 */
export function isMockEnabled(): boolean {
  return process.env['NEXT_PUBLIC_USE_MOCK'] === '1';
}
