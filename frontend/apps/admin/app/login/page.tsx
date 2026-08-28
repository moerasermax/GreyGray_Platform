'use client';

import { Field, Input } from '@greygray/ui/admin';
import { useRouter } from 'next/navigation';
import { useEffect, useState, type FormEvent } from 'react';
import { getSession, login, LoginError } from './_lib/session';

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // 已經登入的人不該再看到登入頁。
  useEffect(() => {
    if (getSession()) {
      router.replace('/');
    }
  }, [router]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
      router.replace('/');
    } catch (cause) {
      setError(cause instanceof LoginError ? cause.message : '登入失敗，請稍後再試。');
    } finally {
      setSubmitting(false);
    }
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
            <Input
              id="login-password"
              type="password"
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

        <div className="mt-6 rounded-sm border border-border-soft bg-surface-sunken p-3 text-xs text-fg-on-tint">
          <p className="font-medium text-fg">開發用測試帳號（暫時假資料，後端串接後移除）</p>
          <p className="mt-1">owner / accountant / operator / readonly@greygray.tw</p>
          <p>密碼：greygray123</p>
        </div>
      </div>
    </main>
  );
}
