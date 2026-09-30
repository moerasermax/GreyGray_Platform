'use client';

import { Field, Input, PasswordInput } from '@greygray/ui/admin';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useState, type FormEvent } from 'react';
import { usePayloadIdempotency } from '../_lib/usePayloadIdempotency';
import { LoginError, login, refreshSession } from './_lib/session';

/** `useSearchParams()` 要求 Suspense 邊界，否則 `next build` 靜態化這頁時會報錯。 */
export default function LoginPage() {
  return (
    <Suspense fallback={<div className="min-h-screen bg-bg" />}>
      <LoginContent />
    </Suspense>
  );
}

function LoginContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  // middleware 沒帶 cookie 的請求導來這裡時會附上 `from`，登入成功後導回那裡。
  const from = searchParams.get('from');
  const redirectTo = from && from.startsWith('/') && !from.startsWith('/login') ? from : '/';

  const idempotency = usePayloadIdempotency();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [checkingSession, setCheckingSession] = useState(true);

  // 已經登入的人不該再看到登入頁——但「有沒有登入」要問後端，不能用本地快取猜。
  useEffect(() => {
    let cancelled = false;
    refreshSession()
      .then((session) => {
        if (cancelled) return;
        if (session) {
          router.replace(redirectTo);
          return;
        }
        setCheckingSession(false);
      })
      .catch(() => {
        if (!cancelled) setCheckingSession(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    const payload = { email, password };
    try {
      await login(payload.email, payload.password, idempotency.current(payload));
      idempotency.complete();
      router.replace(redirectTo);
    } catch (cause) {
      setError(cause instanceof LoginError ? cause.message : '登入失敗，請稍後再試。');
    } finally {
      setSubmitting(false);
    }
  }

  if (checkingSession) {
    return <div className="min-h-screen bg-bg" />;
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-bg px-4">
      <div className="w-full max-w-sm rounded-lg border border-border-soft bg-surface p-6 shadow-popover">
        <h1 className="text-xl font-semibold text-fg">GreyGray 後台</h1>
        <p className="mt-1 text-sm text-fg-muted">團隊成員登入</p>

        <form className="mt-6 flex flex-col gap-4" onSubmit={handleSubmit} noValidate>
          <Field label="Email" htmlFor="login-email" required>
            <Input
              id="login-email"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              invalid={Boolean(error)}
            />
          </Field>
          <Field label="密碼" htmlFor="login-password" required error={error ?? undefined}>
            <PasswordInput
              id="login-password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              invalid={Boolean(error)}
            />
          </Field>

          <button
            type="submit"
            disabled={submitting}
            className="mt-2 rounded-full bg-primary px-4 py-2 text-sm font-semibold text-on-primary hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-60"
          >
            {submitting ? '登入中…' : '登入'}
          </button>
        </form>
      </div>
    </main>
  );
}
