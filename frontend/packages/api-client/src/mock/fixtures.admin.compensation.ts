/**
 * 缺貨（unavailable）與漲價（price-changed）的 mock 常數（M1b，FE-14）。
 *
 * 這兩支端點本身不回傳完整的 `PurchaseItem`——`unavailable` 只回 200，
 * `price-changed` 只回 `{ inquiryId, timeoutAt }`。畫面在收到成功回應後
 * 直接把該筆項目的狀態改在本地（見 `procurement/[campaignId]/page.tsx`），
 * 不是靠重新 `GET` 清單反映——那份清單狀態是 `handlers.admin.procurement.ts`
 * （FE-11 的檔案）自己管的私有變數，不在這一包的派工範圍內，兩邊的 mock 狀態不同步
 * 是已知落差，回報給整合者，見交付清單。
 *
 * 所以這裡沒有假資料清單，只放跟契約行為有關、handlers／測試都要用到的常數。
 */

/** 逾時視為照買的等待窗口。契約沒有寫死小時數，這裡挑一個好展示、好測試的值。 */
export const PRICE_CHANGE_TIMEOUT_HOURS = 24;
