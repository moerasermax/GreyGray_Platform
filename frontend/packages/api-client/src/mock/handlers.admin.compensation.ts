/**
 * 缺貨（unavailable）與漲價（price-changed）的 MSW handlers（M1b，FE-14）。
 * 覆蓋 `docs/15` FE-14 的兩支端點：
 * `POST /v1/purchase-items/{purchaseItemId}/unavailable`、
 * `POST /v1/purchase-items/{purchaseItemId}/price-changed`。
 *
 * 由 `handlers.admin.ts`（FE-9 的檔案）在 `adminHandlers` 陣列尾端展開進去，
 * 這裡不動那個檔案，只匯出 `adminCompensationHandlers`（跟 `adminProcurementHandlers` 同一套慣例）。
 *
 * 兩支都是無狀態 mock：不維護自己的一份 `PurchaseItem[]`，因為那會跟
 * `handlers.admin.procurement.ts` 的清單狀態脫鉤，看起來像是「標了缺貨但清單沒變」。
 * 畫面拿到成功回應後自己把該筆狀態改在本地（見 `[campaignId]/page.tsx`）。
 */
import { http, HttpResponse } from 'msw';
import type { components } from '../types.admin';
import { hexId } from './ids';
import { PRICE_CHANGE_TIMEOUT_HOURS } from './fixtures.admin.compensation';
import { problem, jsonProblem } from './problems';

type S = components['schemas'];

/** 跟 `handlers.admin.procurement.ts` 一樣寫死，理由也一樣：避免循環 import。 */
const ADMIN_BASE_URL = 'http://localhost:5001';

function url(path: string): string {
  return `${ADMIN_BASE_URL}${path}`;
}

interface MarkUnavailableBody {
  readonly reason: string;
}

interface ReportPriceChangedBody {
  readonly newPrice: S['Money'];
}

export const adminCompensationHandlers = [
  http.post(url('/v1/purchase-items/:purchaseItemId/unavailable'), async ({ request }) => {
    const body = (await request.json()) as MarkUnavailableBody;
    if (!body.reason?.trim()) {
      return jsonProblem(problem(422, 'procurement.unavailable-reason-required', '請填寫缺貨原因。'));
    }
    return new HttpResponse(null, { status: 200 });
  }),

  http.post(url('/v1/purchase-items/:purchaseItemId/price-changed'), async ({ request, params }) => {
    const body = (await request.json()) as ReportPriceChangedBody;
    const timeoutAt = new Date(Date.now() + PRICE_CHANGE_TIMEOUT_HOURS * 3_600_000).toISOString();
    return HttpResponse.json({
      inquiryId: hexId(`inquiry:${String(params.purchaseItemId)}:${body.newPrice.amountMinor}:${body.newPrice.currency}`),
      timeoutAt,
    });
  }),
];

/** 未在成功清單覆蓋、但自驗要看到的錯誤情境：後端拒絕漲價回報。 */
export const adminCompensationErrorScenarios = {
  priceChangedRejected: http.post(url('/v1/purchase-items/:purchaseItemId/price-changed'), () =>
    jsonProblem(
      problem(422, 'procurement.price-change-rejected', '這筆採購目前無法回報漲價。', {
        detail: '這個品項已經被標記為缺貨，不能再回報漲價，請重新整理清單。',
      }),
    ),
  ),
  unavailableRejected: http.post(url('/v1/purchase-items/:purchaseItemId/unavailable'), () =>
    jsonProblem(
      problem(422, 'procurement.unavailable-rejected', '這筆採購目前無法標記缺貨。', {
        detail: '這個品項已經回報買到，不能再標記缺貨，請重新整理清單。',
      }),
    ),
  ),
};
