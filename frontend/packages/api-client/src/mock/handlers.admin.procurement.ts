/**
 * 現場採購清單（M1b-1）的 MSW handlers。覆蓋 `docs/12` FE-11 契約的兩支端點：
 * `GET /v1/campaigns/{campaignId}/purchase-items`、`POST /v1/purchase-items/{purchaseItemId}/purchased`。
 *
 * 由 `handlers.admin.ts`（FE-9 的檔案）在 `adminHandlers` 陣列尾端展開進去，
 * 這裡不動那個檔案，只匯出 `adminProcurementHandlers`。
 */
import { http, HttpResponse } from 'msw';
import type { components } from '../types.admin';
import { purchaseItemsByCampaignId } from './fixtures.admin.procurement';
import { problem, jsonProblem } from './problems';

type S = components['schemas'];

/**
 * 跟 `handlers.admin.ts` 一樣寫死，不從那個檔案 import——
 * 那個檔案反過來會 import 這裡的 `adminProcurementHandlers`，import 回去會變成循環依賴，
 * 實測會在 Next dev instrumentation 階段炸「Cannot access 'ADMIN_BASE_URL' before initialization」。
 * `handlers.admin.auth.ts` 也是同樣的理由各自寫一份。
 */
const ADMIN_BASE_URL = 'http://localhost:5001';

function url(path: string): string {
  return `${ADMIN_BASE_URL}${path}`;
}

let purchaseItems = cloneFixture();

function cloneFixture(): Map<string, S['PurchaseItem'][]> {
  return new Map(
    Array.from(purchaseItemsByCampaignId.entries()).map(([campaignId, items]) => [
      campaignId,
      items.map((item) => ({ ...item })),
    ]),
  );
}

/** 測試之間重置 mock 的可變狀態。 */
export function resetAdminProcurementMockState(): void {
  purchaseItems = cloneFixture();
}

interface ReportPurchasedBody {
  readonly quantityPurchased: number;
  readonly actualPaidOriginal: S['Money'];
  readonly actualPaidBooking: S['Money'];
}

export const adminProcurementHandlers = [
  http.get(url('/v1/campaigns/:campaignId/purchase-items'), ({ request, params }) => {
    const items = purchaseItems.get(String(params.campaignId)) ?? [];
    const status = new URL(request.url).searchParams.get('status') as S['PurchaseItemStatus'] | null;
    const filtered = status ? items.filter((item) => item.status === status) : items;
    return HttpResponse.json(filtered);
  }),

  http.post(url('/v1/purchase-items/:purchaseItemId/purchased'), async ({ request, params }) => {
    const body = (await request.json()) as ReportPurchasedBody;
    let found: S['PurchaseItem'] | null = null;
    let ownerCampaignId: string | null = null;
    for (const [campaignId, items] of purchaseItems.entries()) {
      const item = items.find((candidate) => candidate.id === params.purchaseItemId);
      if (item) {
        found = item;
        ownerCampaignId = campaignId;
        break;
      }
    }
    if (!found || !ownerCampaignId) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這個採購項目。'));
    }
    if (body.quantityPurchased !== found.quantityRequested) {
      return jsonProblem(
        problem(422, 'procurement.partial-purchase-not-supported', '這一版只接受全數買到。', {
          detail: `需求數量 ${found.quantityRequested}，回報數量 ${body.quantityPurchased}，目前不支援部分買到。`,
        }),
      );
    }
    if (body.actualPaidBooking.currency !== 'TWD') {
      return jsonProblem(
        problem(422, 'procurement.booking-currency-must-be-twd', '記帳幣必須是 TWD。', {
          detail: `收到的記帳幣是 ${body.actualPaidBooking.currency}。`,
        }),
      );
    }
    const updated: S['PurchaseItem'] = {
      ...found,
      quantityPurchased: body.quantityPurchased,
      status: 'Purchased',
      decidedAt: new Date().toISOString(),
    };
    purchaseItems.set(
      ownerCampaignId,
      (purchaseItems.get(ownerCampaignId) ?? []).map((item) => (item.id === updated.id ? updated : item)),
    );
    return new HttpResponse(null, { status: 200 });
  }),
];

/** 未在成功清單覆蓋、但自驗要看到的錯誤情境：強制「回報買到」失敗。 */
export const adminProcurementErrorScenarios = {
  purchasedRejected: http.post(url('/v1/purchase-items/:purchaseItemId/purchased'), () =>
    jsonProblem(
      problem(422, 'procurement.purchase-rejected', '這筆採購目前無法回報買到。', {
        detail: '這個品項已經被標記為缺貨，不能再回報買到，請重新整理清單。',
      }),
    ),
  ),
};
