'use client';

import { Fragment, type ReactNode } from 'react';
import { EmptyState } from './EmptyState';
import { SortIcon } from './icons';

export type SortDirection = 'asc' | 'desc';

export interface DataTableColumn<T> {
  readonly key: string;
  readonly header: ReactNode;
  /** 必須回傳完整的 `<td>`——`MoneyCell`／`DirectionCell` 本身就是 `<td>`。 */
  readonly renderCell: (row: T) => ReactNode;
  readonly headerAlign?: 'left' | 'right';
  readonly sortable?: boolean;
  readonly width?: string;
}

export interface DataTableProps<T> {
  readonly columns: ReadonlyArray<DataTableColumn<T>>;
  readonly rows: readonly T[];
  readonly getRowKey: (row: T) => string;
  readonly loading?: boolean;
  /** 密排列高（`--ga-row-height-dense`），列表很長時用。 */
  readonly dense?: boolean;
  readonly sortKey?: string;
  readonly sortDirection?: SortDirection;
  readonly onSortChange?: (key: string) => void;
  readonly onRowClick?: (row: T) => void;
  readonly emptyTitle?: string;
  readonly emptyDescription?: string;
  readonly emptyAction?: ReactNode;
  /** 骨架列數，預設跟目前列數走，兩者都沒有就 6 列。 */
  readonly skeletonRows?: number;
}

export function DataTable<T>({
  columns,
  rows,
  getRowKey,
  loading = false,
  dense = false,
  sortKey,
  sortDirection,
  onSortChange,
  onRowClick,
  emptyTitle = '目前沒有資料',
  emptyDescription,
  emptyAction,
  skeletonRows,
}: DataTableProps<T>) {
  const rowHeight = dense ? 'h-[var(--ga-row-height-dense)]' : 'h-[var(--ga-row-height)]';
  const isEmpty = !loading && rows.length === 0;

  return (
    <div className="overflow-x-auto rounded-card border border-border-soft bg-surface shadow-card">
      <table className="w-full min-w-full border-collapse text-sm">
        <thead>
          <tr className="border-b border-border-soft bg-surface-sunken text-left text-fg-on-tint">
            {columns.map((column) => {
              const isSorted = sortKey === column.key;
              return (
                <th
                  key={column.key}
                  scope="col"
                  style={column.width ? { width: column.width } : undefined}
                  className={`px-3 py-2 text-xs font-medium tracking-wide ${
                    column.headerAlign === 'right' ? 'text-right' : 'text-left'
                  }`}
                  data-numeric={column.headerAlign === 'right' ? true : undefined}
                >
                  {column.sortable ? (
                    <button
                      type="button"
                      onClick={() => onSortChange?.(column.key)}
                      className={`inline-flex items-center gap-1 hover:text-fg ${
                        column.headerAlign === 'right' ? 'flex-row-reverse' : ''
                      } ${isSorted ? 'text-fg' : ''}`}
                    >
                      <span>{column.header}</span>
                      <SortIcon
                        className={
                          isSorted && sortDirection === 'desc' ? 'rotate-180 text-fg' : ''
                        }
                      />
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {loading
            ? Array.from({ length: skeletonRows ?? Math.max(rows.length, 6) }).map((_, index) => (
                // 骨架列沒有穩定 id 可用，索引在這裡是安全的——列表在 loading 完成前不會重排。
                // eslint-disable-next-line react/no-array-index-key
                <tr key={index} className={`border-b border-border-soft last:border-0 ${rowHeight}`}>
                  {columns.map((column) => (
                    <td key={column.key} className="px-3 py-2">
                      <div className="h-4 w-full max-w-32 animate-pulse rounded-sm bg-surface-sunken" />
                    </td>
                  ))}
                </tr>
              ))
            : isEmpty
              ? (
                  <tr>
                    <td colSpan={columns.length} className="p-0">
                      <EmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} />
                    </td>
                  </tr>
                )
              : rows.map((row) => (
                  <tr
                    key={getRowKey(row)}
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                    className={`border-b border-border-soft last:border-0 ${rowHeight} ${
                      onRowClick ? 'cursor-pointer hover:bg-surface-sunken' : ''
                    }`}
                  >
                    {columns.map((column) => (
                      <Fragment key={column.key}>{column.renderCell(row)}</Fragment>
                    ))}
                  </tr>
                ))}
        </tbody>
      </table>
    </div>
  );
}
