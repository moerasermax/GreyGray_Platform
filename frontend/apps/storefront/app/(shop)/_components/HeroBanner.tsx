import Link from 'next/link';
import type { components } from '@greygray/api-client/storefront';
import { Thumbnail } from '@greygray/ui';

type S = components['schemas'];

const FOCUS_RING =
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-strong';

const CTA_BASE = `inline-flex min-h-[var(--gg-touch-min)] items-center justify-center rounded-pill px-[var(--gg-space-5)] text-[length:var(--gg-text-sm)] font-bold transition-colors duration-[var(--gg-duration-fast)] ${FOCUS_RING}`;
const CTA_PRIMARY = `${CTA_BASE} bg-primary text-on-primary hover:bg-primary-hover`;
const CTA_SECONDARY = `${CTA_BASE} border border-border-strong bg-surface text-fg hover:border-primary-strong hover:text-primary-text`;

/**
 * 首頁主視覺（ADR-009 首頁切版第二段；FE-38 改版）。
 *
 * 有開團中的旅程 → 開團封面 ＋ 進團入口；**沒有也不消失**——改放品牌介紹與瀏覽入口，
 * 否則沒有活動的那幾天，首頁最上面就只剩一條搜尋列。
 * 文案只講站上查得到的事：不放數據、不放保證、不放假促銷。
 */
export function HeroBanner({ campaign }: { campaign: S['CampaignListItem'] | undefined }) {
  return (
    <section
      aria-labelledby="home-hero-title"
      className="grid overflow-hidden rounded-[var(--gg-radius-xl)] border border-border-soft shadow-card lg:min-h-[var(--gg-hero-min-h-lg)] lg:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]"
      style={{ background: 'var(--gg-gradient-banner)' }}
    >
      <div className="flex min-w-0 flex-col justify-center gap-[var(--gg-space-4)] p-[var(--gg-space-5)] lg:p-[var(--gg-space-7)]">
        <p className="flex items-center gap-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] font-bold tracking-[var(--gg-tracking-eyebrow)] text-primary-text">
          <span aria-hidden="true" className="h-[var(--gg-space-2)] w-[var(--gg-space-2)] rounded-pill bg-primary" />
          {campaign ? '現正開團' : 'GREYGRAY 選品代購'}
        </p>

        <h2
          id="home-hero-title"
          className="max-w-[var(--gg-hero-text-max)] font-display text-[length:var(--gg-text-2xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg [overflow-wrap:anywhere] lg:text-[length:var(--gg-text-3xl)]"
        >
          {campaign ? campaign.title : '出國採購開團，加上本地現貨選品'}
        </h2>

        <p className="max-w-[var(--gg-hero-text-max)] text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg-muted [overflow-wrap:anywhere]">
          {campaign
            ? `${campaign.destination}出發的代購團，現在開放登記。`
            : '目前沒有開放登記的代購團。可以先逛現貨商品，或到開團頁查看開團狀態。'}
        </p>

        <div className="flex flex-wrap gap-[var(--gg-space-3)] pt-[var(--gg-space-2)]">
          {campaign ? (
            <>
              <Link href={`/campaigns/${campaign.id}`} className={CTA_PRIMARY}>
                查看這一團
              </Link>
              <Link href="/products" className={CTA_SECONDARY}>
                逛全部商品
              </Link>
            </>
          ) : (
            <>
              <Link href="/products" className={CTA_PRIMARY}>
                逛全部商品
              </Link>
              <Link href="/campaigns" className={CTA_SECONDARY}>
                查看開團
              </Link>
            </>
          )}
        </div>
      </div>

      <div className="relative h-[var(--gg-hero-media-h)] min-w-0 border-t border-border-soft lg:h-auto lg:border-l lg:border-t-0">
        {campaign?.coverImageUrl ? (
          <div className="absolute inset-0 overflow-hidden">
            <Thumbnail
              src={campaign.coverImageUrl}
              alt={`${campaign.destination}開團封面`}
              sizes="(max-width: 1024px) 100vw, 560px"
            />
          </div>
        ) : (
          <HeroArt
            label={campaign ? campaign.destination : 'GreyGray'}
            caption={campaign ? '代購開團' : '選品代購'}
          />
        )}
      </div>
    </section>
  );
}

/**
 * 沒有封面圖時的替代畫面：純 CSS 的同心圓 ＋ 文字排版。
 * **不放任何假圖片**——缺圖就誠實地用排版撐版面，而不是留一塊空白色塊。
 * 整塊是裝飾（同樣的字在左邊文字欄已經唸過一次），所以對輔助科技隱藏。
 */
function HeroArt({ label, caption }: { label: string; caption: string }) {
  return (
    <div aria-hidden="true" className="absolute inset-0 overflow-hidden bg-surface">
      <div className="absolute right-0 top-1/2 aspect-square h-[150%] -translate-y-1/2 translate-x-1/4">
        <div className="absolute inset-0 rounded-pill border border-border-soft" />
        <div className="absolute inset-[14%] rounded-pill border border-border-strong" />
        <div className="absolute inset-[28%] rounded-pill bg-primary-subtle" />
        <div className="absolute inset-[44%] rounded-pill border border-secondary" />
      </div>

      <div className="absolute inset-0 flex flex-col justify-end gap-[var(--gg-space-2)] p-[var(--gg-space-5)] lg:p-[var(--gg-space-6)]">
        <span className="text-[length:var(--gg-text-xs)] font-bold tracking-[var(--gg-tracking-eyebrow)] text-fg-muted">
          GREYGRAY · {caption}
        </span>
        <span className="line-clamp-2 font-display text-[length:var(--gg-text-3xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg [overflow-wrap:anywhere]">
          {label}
        </span>
        <span className="h-[var(--gg-space-1)] w-[var(--gg-space-7)] rounded-pill bg-primary" />
      </div>
    </div>
  );
}
