'use client';

import { formatMoney } from '@greygray/api-client';
import { createLot, listLots } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { DataTable, Drawer, Field, Input, MoneyCell, type DataTableColumn } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { apiErrorMessage, apiFieldErrors, fieldErrorText } from '../_lib/apiError';
import { EMPTY_LOT_FORM, buildLotInput, type LotFormFields } from '../_lib/lotForm';
import { Button } from './Button';

type S = components['schemas'];

export interface LotDrawerProps {
  readonly open: boolean;
  readonly sku: S['AdminSku'] | null;
  readonly onClose: () => void;
  /** 進貨成功後叫一次——商品頁要重新 GET，因為 SKU 的「可用量」變了。 */
  readonly onReceived: () => void;
}

/**
 * 批發進貨（M2 `POST /v1/lots`）＋ 該 SKU 的批號列表（`GET /v1/lots?skuId=`）。
 *
 * **批號是成本的載體**（契約原話）：同一個 SKU 進兩次貨、成本不同，就是兩個批號，
 * 出貨時從指定批號結轉銷貨成本。所以這裡不是「改一個庫存數字」，是「新增一筆進貨」——
 * 沒有修改、沒有刪除，M2 契約也只有這兩支。要退貨或調整請等對應的端點。
 *
 * 可用量一律讀後端回的 `quantityAvailable`／`available`，**前端不做任何庫存運算**。
 */
export function LotDrawer({ open, sku, onClose, onReceived }: LotDrawerProps) {
  const idempotency = usePayloadIdempotency();
  const [lots, setLots] = useState<readonly S['Lot'][]>([]);
  const [loading, setLoading] = useState(false);
  const [listError, setListError] = useState<string | null>(null);
  const [fields, setFields] = useState<LotFormFields>(EMPTY_LOT_FORM);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [reloadKey, setReloadKey] = useState(0);

  // 依 id 而不是物件：進貨成功後商品重新 GET，`sku` 會是一個內容相同的新物件，
  // 用物件當相依會讓表單被清空、批號也白抓一次。
  const skuId = sku?.id ?? null;

  useEffect(() => {
    if (!open || !skuId) return;
    setFields(EMPTY_LOT_FORM);
    setError(null);
    setFieldErrors({});
  }, [open, skuId]);

  useEffect(() => {
    if (!open || !skuId) return;
    let cancelled = false;
    setLoading(true);
    setListError(null);
    void listLots(browserApi(), { skuId })
      .then((page) => {
        if (!cancelled) setLots(page.items);
      })
      .catch((cause: unknown) => {
        if (!cancelled) {
          setLots([]);
          setListError(apiErrorMessage(cause));
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [open, skuId, reloadKey]);

  function patch(changes: Partial<LotFormFields>) {
    setFields((current) => ({ ...current, ...changes }));
  }

  async function handleSubmit() {
    if (!sku) return;
    const built = buildLotInput(sku.id, fields);
    if (!built.ok) {
      setError(built.message);
      return;
    }
    setSubmitting(true);
    setError(null);
    setFieldErrors({});
    try {
      const body = built.body;
      await createLot(browserApi(), body, { idempotencyKey: idempotency.current({ body }) });
      idempotency.complete();
      setFields(EMPTY_LOT_FORM);
      // 寫入完成後重新 GET，不要拿 request 內容當成新狀態（docs/05 §9）。
      setReloadKey((current) => current + 1);
      onReceived();
    } catch (cause) {
      setError(apiErrorMessage(cause));
      setFieldErrors(apiFieldErrors(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={sku ? `進貨：${sku.name}` : '進貨'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            關閉
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting || !sku}>
            {submitting ? '建立中…' : '建立批號'}
          </Button>
        </>
      }
    >
      <LotDrawerBody
        sku={sku}
        lots={lots}
        loading={loading}
        listError={listError}
        fields={fields}
        error={error}
        fieldErrors={fieldErrors}
        onChange={patch}
      />
    </Drawer>
  );
}

export interface LotDrawerBodyProps {
  readonly sku: S['AdminSku'] | null;
  readonly lots: readonly S['Lot'][];
  readonly loading: boolean;
  readonly listError: string | null;
  readonly fields: LotFormFields;
  readonly error: string | null;
  readonly fieldErrors: Record<string, string[]>;
  readonly onChange: (changes: Partial<LotFormFields>) => void;
}

/** Drawer 的內容拆成純呈現，這樣沒有 jsdom 也驗得到「畫出來的就是端點回的」。 */
export function LotDrawerBody({
  sku,
  lots,
  loading,
  listError,
  fields,
  error,
  fieldErrors,
  onChange,
}: LotDrawerBodyProps) {
  return (
    <div className="flex flex-col gap-5">
      <section className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-fg-muted">
          既有批號{sku ? `（目前可用量 ${sku.available}）` : ''}
        </h3>
        {listError ? (
          <p role="alert" className="text-sm font-medium text-danger">
            {listError}
          </p>
        ) : (
          <DataTable
            columns={LOT_COLUMNS}
            rows={lots}
            getRowKey={(row) => row.id}
            loading={loading}
            dense
            skeletonRows={2}
            emptyTitle="還沒有任何批號"
            emptyDescription="這個 SKU 還沒進過貨，所以可用量是 0。用下面的表單建第一筆。"
          />
        )}
      </section>

      <section className="flex flex-col gap-4">
        <h3 className="text-sm font-semibold text-fg-muted">新增進貨</h3>
        {error ? (
          <p role="alert" className="text-sm font-medium text-danger">
            {error}
          </p>
        ) : null}

        <Field label="數量" htmlFor="lot-quantity" required hint="整數，至少 1" error={fieldErrorText(fieldErrors, 'quantity')}>
          <Input
            id="lot-quantity"
            type="number"
            min={1}
            step={1}
            value={fields.quantity}
            onChange={(event) => onChange({ quantity: event.target.value })}
          />
        </Field>

        <Field
          label="單位成本（元）"
          htmlFor="lot-unit-cost"
          required
          hint="這一批的實際進價。批號別成本，出貨時從指定批號結轉。"
          error={fieldErrorText(fieldErrors, 'unitCost')}
        >
          <Input
            id="lot-unit-cost"
            type="number"
            min={0}
            value={fields.unitCostMajor}
            onChange={(event) => onChange({ unitCostMajor: event.target.value })}
          />
        </Field>

        <Field
          label="批號"
          htmlFor="lot-batch-code"
          hint="選填，例如廠商的批號或到貨日"
          error={fieldErrorText(fieldErrors, 'batchCode')}
        >
          <Input id="lot-batch-code" value={fields.batchCode} onChange={(event) => onChange({ batchCode: event.target.value })} />
        </Field>
      </section>
    </div>
  );
}

const LOT_COLUMNS: DataTableColumn<S['Lot']>[] = [
  {
    key: 'quantityOnHand',
    header: '數量',
    headerAlign: 'right',
    renderCell: (row) => (
      <td data-numeric className="gg-numeric px-3 py-2">
        {row.quantityOnHand}
      </td>
    ),
  },
  {
    key: 'quantityAvailable',
    header: '可用',
    headerAlign: 'right',
    renderCell: (row) => (
      <td data-numeric className="gg-numeric px-3 py-2">
        {row.quantityAvailable}
      </td>
    ),
  },
  {
    key: 'unitCost',
    header: '單位成本',
    headerAlign: 'right',
    renderCell: (row) => <MoneyCell value={formatMoney(row.unitCost)} />,
  },
  {
    key: 'batchCode',
    header: '批號',
    renderCell: (row) => <td className="px-3 py-2 text-fg-muted">{row.batchCode ?? '—'}</td>,
  },
  {
    key: 'receivedAt',
    header: '進貨時間',
    renderCell: (row) => (
      <td className="px-3 py-2 text-fg-muted">
        {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(row.receivedAt))}
      </td>
    ),
  },
];
