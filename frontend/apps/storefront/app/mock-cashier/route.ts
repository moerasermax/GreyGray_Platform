/*
 * 假的綠界收銀台。**只在 mock 模式存在。**
 *
 * 為什麼需要它：付款是「用 `PaymentInitiation` 建 hidden form，把 `fields` 原封不動
 * POST 到 `action`」（docs/06 FE-4）。那是一次**導覽**，不是 XHR——
 * msw 的 service worker 只攔得到自己 scope 內的請求，攔不到跨來源的表單 POST。
 * 所以 mock 的 `action` 如果指向真的綠界，本機跑一次結帳就真的送一包資料出去，
 * 而且拿回「CheckMacValue Error」的錯誤頁，看起來很像前端自己壞掉。
 * 第二波整合測試就是這樣撞到的。
 *
 * 這支 route handler 站在 app 自己的來源上，接住那個 POST 再導回結果頁。
 * 關掉 mock 時回 404——正式環境不該存在這條路徑。
 */
import { NextResponse } from 'next/server';

export async function POST(request: Request): Promise<Response> {
  if (process.env['NEXT_PUBLIC_USE_MOCK'] !== '1') {
    return new NextResponse('Not Found', { status: 404 });
  }

  const orderId = new URL(request.url).searchParams.get('orderId');
  if (!orderId) {
    return new NextResponse('mock-cashier 需要 orderId', { status: 400 });
  }

  /*
   * 真的綠界會把付款結果帶在回傳參數裡，但結果頁刻意不相信網址上的任何參數，
   * 一律重新 `GET /v1/orders/{orderId}` 拿後端的狀態（見 payment/result 的檔頭）。
   * 所以這裡只要把人送回去就好，不用假造 RtnCode。
   */
  return NextResponse.redirect(new URL(`/payment/result?orderId=${orderId}`, request.url), 303);
}
