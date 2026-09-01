'use client';

import { ApiError } from '@greygray/api-client';
import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import { Button, Card, Field, Input } from '@greygray/ui';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { usePayloadIdempotency } from '../../_lib/usePayloadIdempotency';

/**
 * 登入失敗一律顯示「手機號碼或密碼錯誤」——**不區分帳號不存在與密碼錯**，
 * 區分了就是送一份帳號列舉工具給對方。這條後端也守著，前端不要自作聰明
 * 去讀 `problem.code` 反推細節再顯示不同文案。
 */
const INVALID_CREDENTIALS_MESSAGE = '手機號碼或密碼錯誤，請再試一次。';
const RATE_LIMITED_MESSAGE = '嘗試次數過多，請稍後再試。';
const UNEXPECTED_MESSAGE = '登入時發生問題，請稍後再試。';

export default function LoginPage() {
  const router = useRouter();
  const [phoneNumber, setPhoneNumber] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const idempotency = usePayloadIdempotency();

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const input = { phoneNumber, password };
      await storefrontApi.login(browserApi(), input, {
        idempotencyKey: idempotency.current(input),
      });
      idempotency.complete();
      router.push('/orders');
    } catch (caught) {
      if (caught instanceof ApiError) {
        if (caught.status === 401) {
          setError(INVALID_CREDENTIALS_MESSAGE);
        } else if (caught.status === 429) {
          setError(RATE_LIMITED_MESSAGE);
        } else {
          setError(UNEXPECTED_MESSAGE);
        }
      } else {
        setError(UNEXPECTED_MESSAGE);
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="mx-auto flex max-w-[420px] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <header className="flex flex-col gap-[var(--gg-space-2)]">
        <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">登入</h1>
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">用手機號碼與密碼登入 GreyGray。</p>
      </header>

      <Card>
        <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-[var(--gg-space-4)]">
          <Field label="手機號碼" htmlFor="login-phone" required>
            <Input
              id="login-phone"
              type="tel"
              inputMode="numeric"
              autoComplete="tel"
              required
              value={phoneNumber}
              onChange={(e) => setPhoneNumber(e.target.value)}
              placeholder="0912345678"
            />
          </Field>

          <Field label="密碼" htmlFor="login-password" required>
            <Input
              id="login-password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>

          {error && (
            <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
              {error}
            </p>
          )}

          <Button type="submit" variant="primary" size="lg" loading={submitting} fullWidth>
            登入
          </Button>
        </form>
      </Card>

      <p className="text-center text-[length:var(--gg-text-sm)] text-fg-muted">
        還沒有帳號？{' '}
        <Link href="/register" className="font-bold text-primary-text">
          註冊新帳號
        </Link>
      </p>
    </main>
  );
}
