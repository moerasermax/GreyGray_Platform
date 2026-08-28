import { formatMoney } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { EmptyState, Field, Select } from '@greygray/ui/admin';

type S = components['schemas'];

export interface CampaignMarginPanelProps {
  readonly campaigns: readonly S['AdminCampaign'][];
  readonly campaignId: string;
  readonly onSelectCampaign: (campaignId: string) => void;
  readonly margin: S['CampaignMargin'] | null;
  readonly loading: boolean;
}

interface MarginRow {
  readonly label: string;
  readonly value: S['Money'];
  readonly emphasis?: boolean;
}

/**
 * 每團毛利。`grossMargin` 是後端算好的——**這裡只負責排版顯示**，
 * 不會把 salesRevenue / costOfGoodsSold 等欄位自己加減一次來對答案。
 */
export function CampaignMarginPanel({ campaigns, campaignId, onSelectCampaign, margin, loading }: CampaignMarginPanelProps) {
  const rows: MarginRow[] | null = margin
    ? [
        { label: '銷售收入', value: margin.salesRevenue },
        { label: '銷貨成本', value: margin.costOfGoodsSold },
        { label: '運費收入', value: margin.shippingRevenue },
        { label: '運費成本', value: margin.shippingCost },
        { label: '旅程成本', value: margin.tripCost },
        { label: '毛利（後端計算）', value: margin.grossMargin, emphasis: true },
      ]
    : null;

  return (
    <section className="flex flex-col gap-3 rounded-card border border-border-soft bg-surface p-5 shadow-card">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h2 className="text-sm font-semibold text-fg-muted">每團毛利</h2>
        <Field label="選擇開團" htmlFor="campaign-margin-select">
          <Select
            id="campaign-margin-select"
            placeholder="請選擇一個開團"
            value={campaignId}
            onChange={(event) => onSelectCampaign(event.target.value)}
            options={campaigns.map((c) => ({ value: c.id, label: c.title }))}
          />
        </Field>
      </div>

      {!campaignId ? (
        <EmptyState title="選一個開團查看毛利" description="毛利數字全部由後端算好回傳，這裡不重新加總。" />
      ) : loading ? (
        <div className="h-32 animate-pulse rounded-card bg-surface-sunken" />
      ) : rows ? (
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
          {rows.map((row) => (
            <div key={row.label} className={row.emphasis ? 'col-span-2 border-t border-border-soft pt-3 sm:col-span-3' : ''}>
              <dt className="text-xs text-fg-muted">{row.label}</dt>
              <dd
                className={`gg-numeric text-right text-lg font-semibold ${row.emphasis ? 'text-success' : 'text-fg'}`}
              >
                {formatMoney(row.value)}
              </dd>
            </div>
          ))}
        </dl>
      ) : (
        <EmptyState title="查無這個開團的毛利資料" />
      )}
    </section>
  );
}
