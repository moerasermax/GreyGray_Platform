import { formatMoney } from '@greygray/api-client';
import { ErrorState, WarningIcon } from '@greygray/ui/admin';
import type { LiabilityVsCash, LoadState } from '../_lib/dashboardLedger';

export interface LiabilityVsCashCardProps {
  readonly data: LiabilityVsCash;
}

/**
 * 首頁最重要的一塊。`isBreached = true` 代表正在用還沒交貨的錢過日子——
 * 代購生意最典型的崩壞前兆，放在進來第一眼就看得到的位置，不塞進分頁。
 *
 * 這裡只負責畫，`data` 一律由呼叫端從 `GET /v1/ledger/liability-vs-cash` 取得。
 */
export function LiabilityVsCashCard({ data }: LiabilityVsCashCardProps) {
  const asOfLabel = new Intl.DateTimeFormat('zh-TW', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Asia/Taipei',
  }).format(new Date(data.asOf));

  return (
    <section
      className={`rounded-lg border p-5 shadow-card ${
        data.isBreached
          ? 'border-danger/40 bg-danger-subtle'
          : 'border-border-soft bg-surface'
      }`}
      aria-labelledby="liability-vs-cash-heading"
    >
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 id="liability-vs-cash-heading" className="text-sm font-semibold text-fg-on-tint">
            負債 vs 現金
          </h2>
          <p className="mt-0.5 text-xs text-fg-on-tint">資料時間 {asOfLabel}</p>
        </div>

        {data.isBreached ? (
          <div className="flex items-center gap-2 rounded-full bg-danger px-3 py-1 text-sm font-semibold text-bg">
            <WarningIcon className="h-4 w-4" />
            <span>正在用還沒交貨的錢過日子</span>
          </div>
        ) : (
          <div className="rounded-full bg-success-subtle px-3 py-1 text-sm font-semibold text-success">
            現金足以覆蓋客戶負債
          </div>
        )}
      </div>

      <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <p className="text-xs text-fg-on-tint">客戶負債（預收貨款＋預收運費＋儲值金）</p>
          <p className="mt-1 font-mono text-3xl font-semibold tabular-nums text-danger">
            {formatMoney(data.customerLiabilityTotal)}
          </p>
        </div>
        <div>
          <p className="text-xs text-fg-on-tint">現金總額</p>
          <p className="mt-1 font-mono text-3xl font-semibold tabular-nums text-fg">
            {formatMoney(data.cashTotal)}
          </p>
        </div>
      </div>

      {data.isBreached ? (
        <p className="mt-4 text-sm text-danger">
          客戶負債已經超過手上現金，代表有一部分預收款其實已經被挪用。
          在補齊現金或交貨清掉負債之前，這個缺口不會自己消失。
        </p>
      ) : null}
    </section>
  );
}

export interface LiabilityVsCashSectionProps {
  readonly state: LoadState<LiabilityVsCash>;
  readonly onRetry: () => void;
}

/**
 * 三態外殼。**`ready` 以外的分支一個數字都不畫**——
 * 載入中或失敗時退回某個「先擺著」的數字，正是 #29 的成因；
 * 這裡沒有那條路可走，因為 `LoadState` 在型別上就只有 `ready` 拿得到 `data`。
 */
export function LiabilityVsCashSection({ state, onRetry }: LiabilityVsCashSectionProps) {
  if (state.status === 'loading') {
    return (
      <div
        className="h-32 animate-pulse rounded-card bg-surface-sunken"
        role="status"
        aria-label="正在讀取負債 vs 現金"
      />
    );
  }

  if (state.status === 'error') {
    return <ErrorState title={state.title} traceId={state.traceId} onRetry={onRetry} />;
  }

  return <LiabilityVsCashCard data={state.data} />;
}
