/**
 * 出貨單（M1b，`/v1/shipments`）的 MSW handlers。覆蓋 `docs/api/openapi.admin.yaml`
 * `fulfillment` 標籤下的四支端點：`GET /v1/shipments`、`POST /v1/shipments`、
 * `POST /v1/shipments/{id}/dispatch`、`POST /v1/shipments/{id}/deliver`。
 *
 * 由 `handlers.admin.ts`（FE-9 的檔案）在 `adminHandlers` 陣列尾端展開進去，
 * 這裡不動那個檔案，只匯出 `adminShipmentHandlers`。
 *
 * 契約沒有 `GET /v1/shipments/{id}`——只有列表。detail 頁改用列表撈出全部
 * 再從前端找出目標，這裡的 `GET /v1/shipments` 因此要支援夠大的 `limit`。
 */
import { http, HttpResponse } from 'msw';
import type { components } from '../types.admin';
import { adminShipments } from './fixtures.admin.shipments';
import { hexId } from './ids';
import { paginate } from './pagination';
import { problem, jsonProblem } from './problems';

type S = components['schemas'];

/**
 * 跟 `handlers.admin.ts` 一樣寫死，不從那個檔案 import——
 * 那個檔案反過來會 import 這裡的 `adminShipmentHandlers`，import 回去會變成循環依賴
 * （`handlers.admin.procurement.ts` 檔頭已經記錄過這個坑）。
 */
const ADMIN_BASE_URL = 'http://localhost:5001';

function url(path: string): string {
  return `${ADMIN_BASE_URL}${path}`;
}

let shipments: S['AdminShipment'][] = adminShipments.map((s) => ({ ...s }));

/** 測試之間重置 mock 的可變狀態。 */
export function resetAdminShipmentMockState(): void {
  shipments = adminShipments.map((s) => ({ ...s }));
}

const DELIVERED_TERMINAL: ReadonlySet<S['ShipmentStatus']> = new Set(['Delivered', 'Returned', 'Lost']);

interface CreateShipmentBody {
  readonly orderIds: string[];
  readonly method: S['DeliveryMethod'];
}

interface DispatchShipmentBody {
  readonly trackingNumber: string;
  readonly carrierCost: S['Money'];
}

export const adminShipmentHandlers = [
  http.get(url('/v1/shipments'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const status = q.get('status') as S['ShipmentStatus'] | null;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = shipments.filter((s) => !status || s.status === status);
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.post(url('/v1/shipments'), async ({ request }) => {
    const body = (await request.json()) as CreateShipmentBody;
    if (!body.orderIds || body.orderIds.length === 0) {
      return jsonProblem(
        problem(422, 'fulfillment.orders-required', '建立出貨單至少要勾選一張訂單。'),
      );
    }
    const created: S['AdminShipment'] = {
      id: hexId(`admin-shipment:${body.orderIds.join(',')}:${Date.now()}`),
      method: body.method,
      status: 'Draft',
      trackingNumber: null,
      orderIds: body.orderIds,
      carrierCost: null,
      dispatchedAt: null,
      deliveredAt: null,
    };
    shipments = [...shipments, created];
    return HttpResponse.json(created, { status: 201 });
  }),

  http.post(url('/v1/shipments/:shipmentId/dispatch'), async ({ request, params }) => {
    const index = shipments.findIndex((s) => s.id === params.shipmentId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這張出貨單。'));
    const current = shipments[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這張出貨單。'));
    if (DELIVERED_TERMINAL.has(current.status)) {
      return jsonProblem(
        problem(422, 'fulfillment.shipment-already-final', '這張出貨單已經是最終狀態，不能再交運。'),
      );
    }
    const body = (await request.json()) as DispatchShipmentBody;
    const updated: S['AdminShipment'] = {
      ...current,
      status: 'Dispatched',
      trackingNumber: body.trackingNumber,
      carrierCost: body.carrierCost,
      dispatchedAt: new Date().toISOString(),
    };
    shipments = shipments.map((s, i) => (i === index ? updated : s));
    return HttpResponse.json(updated);
  }),

  http.post(url('/v1/shipments/:shipmentId/deliver'), ({ params }) => {
    const index = shipments.findIndex((s) => s.id === params.shipmentId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這張出貨單。'));
    const current = shipments[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這張出貨單。'));
    if (DELIVERED_TERMINAL.has(current.status)) {
      return jsonProblem(
        problem(422, 'fulfillment.shipment-already-final', '這張出貨單已經是最終狀態，不能再標記送達。'),
      );
    }
    const updated: S['AdminShipment'] = {
      ...current,
      status: 'Delivered',
      deliveredAt: new Date().toISOString(),
    };
    shipments = shipments.map((s, i) => (i === index ? updated : s));
    return HttpResponse.json(updated);
  }),
];

/** 未在成功清單覆蓋、但自驗要看到的錯誤情境。 */
export const adminShipmentErrorScenarios = {
  dispatchNotFound: http.post(url('/v1/shipments/:shipmentId/dispatch'), () =>
    jsonProblem(problem(404, 'platform.not-found', '找不到這張出貨單。')),
  ),
  deliverAlreadyFinal: http.post(url('/v1/shipments/:shipmentId/deliver'), () =>
    jsonProblem(problem(422, 'fulfillment.shipment-already-final', '這張出貨單已經是最終狀態，不能再標記送達。')),
  ),
};
