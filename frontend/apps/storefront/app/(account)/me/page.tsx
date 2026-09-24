'use client';

/*
 * 「我的」總覽頁——前台帳號區的**家**。
 *
 * 在這一頁之前，「我的」分頁與首頁頭像都指到 `/orders`（兩個檔的註解自己都寫著
 * 「因為前台沒有帳號首頁」），`/wallet` 一個入口都沒有，而**全站沒有任何地方能登出**。
 * 那是 #30／#32 的第三種形狀：頁面存在，但沒有人連得進去。
 *
 * 版面語彙照 `wallet/page.tsx` 與 `orders/page.tsx`（`Card` ＋ token 間距），
 * 不發明新樣式。分頁列是預設就有的（`_lib/tabs.ts` 用黑名單），
 * 所以這一頁不需要、也不該再加一條頂部列——`_lib/__tests__/pageShell.test.ts`
 * 會替我們檢查「恰好一種殼」。
 */

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { Button, Card, ErrorState, Skeleton } from '@greygray/ui';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { loginHref } from '../../_lib/auth';
import { usePayloadIdempotency } from '../../_lib/usePayloadIdempotency';
import { isUnauthorized } from '../_lib/authRedirect';
import { generalErrorMessage, traceIdOf } from '../_lib/formErrors';
import { performLogout } from './logout';
import { InfoLinks } from '../../(info)/_components/InfoLinks';

type Me = components['schemas']['Me'];

/** 三個入口。放在元件外面是為了讓「有哪些入口」一眼看得完，不用讀 JSX。 */
const ACCOUNT_LINKS: ReadonlyArray<{ href: string; label: string; description: string }> = [
  { href: '/orders', label: '我的訂單', description: '查看訂單狀態、付款與取消' },
  { href: '/addresses', label: '收件地址', description: '管理宅配用的收件地址' },
  { href: '/wallet', label: '儲值金', description: '查看目前的儲值金餘額' },
  { href: '/favorites', label: '我的最愛', description: '收藏的商品' },
];

const LOGOUT_FAILED_MESSAGE = '登出時發生問題，請稍後再試。';

export default function MePage() {
  const router = useRouter();
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const logoutIdempotency = usePayloadIdempotency();

  const load = () => {
    setLoading(true);
    setError(null);
    storefrontApi
      .getMe(browserApi())
      .then(setMe)
      .catch((caught: unknown) => {
        if (isUnauthorized(caught)) {
          // 登入完要回到這一頁，不是被丟去某個預設頁再自己找回來。
          router.replace(loginHref('/me'));
          return;
        }
        setError(caught);
      })
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router]);

  async function handleLogout() {
    setLoggingOut(true);
    setLogoutError(null);
    /*
     * 順序（成功 → 徽章歸零 → 導向）與失敗分支都在 `./logout.ts` 裡，
     * 那支有測試釘住；這裡只負責畫面狀態。
     */
    const ok = await performLogout({
      logout: async () => {
        const payload = { action: 'logout' };
        await storefrontApi.logout(browserApi(), {
          idempotencyKey: logoutIdempotency.current(payload),
        });
        logoutIdempotency.complete();
      },
      /*
       * `replace` 而不是 `push`：登出之後按上一頁不該回到這一頁。
       * 回首頁而不是登入頁——沒登入的人在這個站還是能逛。
       */
      goHome: () => router.replace('/'),
    });

    if (!ok) {
      /*
       * **失敗就留在原地並說出來。** 不要「反正前端把畫面切成未登入」——
       * session cookie 還在的話那是一句不成立的話，而使用者可能正在
       * 別人的手機上按這顆按鈕。徽章也維持原值（見 `./logout.ts`）。
       */
      setLogoutError(LOGOUT_FAILED_MESSAGE);
      setLoggingOut(false);
    }
  }

  return (
    <main className="mx-auto flex max-w-[640px] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">我的</h1>

      {loading && <Skeleton variant="block" className="h-[120px] w-full" />}

      {!loading && error != null && (
        <Card padding="none">
          <ErrorState title={generalErrorMessage(error)} traceId={traceIdOf(error)} onRetry={load} />
        </Card>
      )}

      {!loading && !error && me && (
        <>
          {/*
           * 名稱與 email 是使用者自己填的，長度沒有上限可以假設。
           * `Card` 與全域樣式都沒有換行保護（一長串沒有空白的 email 會直接撐出卡片），
           * 所以在這裡就地 `overflow-wrap:anywhere`——完整顯示、必要時換行，不截斷。
           */}
          <Card className="flex flex-col gap-[var(--gg-space-3)]">
            <p className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg [overflow-wrap:anywhere]">
              {me.displayName}
            </p>
            {(me.phoneNumberMasked || me.email) && (
              <dl className="flex flex-col gap-[var(--gg-space-2)] border-t border-border-soft pt-[var(--gg-space-3)]">
                {me.phoneNumberMasked && (
                  <div className="flex flex-col">
                    <dt className="text-[length:var(--gg-text-xs)] text-fg-muted">手機</dt>
                    <dd className="text-[length:var(--gg-text-sm)] text-fg">{me.phoneNumberMasked}</dd>
                  </div>
                )}
                {me.email && (
                  <div className="flex min-w-0 flex-col">
                    <dt className="text-[length:var(--gg-text-xs)] text-fg-muted">Email</dt>
                    <dd className="text-[length:var(--gg-text-sm)] text-fg [overflow-wrap:anywhere]">
                      {me.email}
                    </dd>
                  </div>
                )}
              </dl>
            )}
          </Card>

          <nav aria-label="帳號功能" className="flex flex-col gap-[var(--gg-space-3)]">
            {ACCOUNT_LINKS.map((link) => (
              /*
               * `block rounded-card`：`<a>` 預設是 inline，包著 block 的卡片時焦點框會碎掉；
               * 全域 `:focus-visible` 又把圓角設成 `radius-sm`，跟 16px 的卡片對不上。
               */
              <Link key={link.href} href={link.href} className="block rounded-card">
                <Card className="flex flex-col gap-[var(--gg-space-1)] transition-colors duration-[var(--gg-duration-fast)] ease-out-soft hover:bg-surface-sunken">
                  <p className="font-bold text-fg">{link.label}</p>
                  <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{link.description}</p>
                </Card>
              </Link>
            ))}
          </nav>

          <Card className="flex flex-col gap-[var(--gg-space-2)]">
            <p className="font-bold text-fg">幫助與資訊</p>
            <InfoLinks />
          </Card>

          <div className="flex flex-col gap-[var(--gg-space-2)]">
            {logoutError && (
              <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
                {logoutError}
              </p>
            )}
            <Button variant="secondary" size="lg" fullWidth loading={loggingOut} onClick={handleLogout}>
              登出
            </Button>
          </div>
        </>
      )}
    </main>
  );
}
