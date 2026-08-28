'use client';

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import { Button, Card, Field, Input } from '@greygray/ui';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { usePayloadIdempotency } from '../../_lib/usePayloadIdempotency';
import { fieldErrorsFrom, generalErrorMessage } from '../_lib/formErrors';
import {
  REGISTER_INITIAL_VALUES,
  type RegisterFieldErrors,
  type RegisterFormValues,
  toRegisterRequest,
  validateRegisterForm,
} from '../_lib/registerSchema';

export default function RegisterPage() {
  const router = useRouter();
  const [values, setValues] = useState<RegisterFormValues>(REGISTER_INITIAL_VALUES);
  const [errors, setErrors] = useState<RegisterFieldErrors>({});
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const idempotency = usePayloadIdempotency();

  function setField<K extends keyof RegisterFormValues>(key: K, value: RegisterFormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }));
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setGeneralError(null);

    const clientErrors = validateRegisterForm(values);
    if (Object.keys(clientErrors).length > 0) {
      setErrors(clientErrors);
      return;
    }

    setSubmitting(true);
    try {
      const request = toRegisterRequest(values);
      await storefrontApi.register(browserApi(), request, {
        idempotencyKey: idempotency.current(request),
      });
      idempotency.complete();
      router.push('/orders');
    } catch (error) {
      const fromApi = fieldErrorsFrom(error);
      if (Object.keys(fromApi).length > 0) {
        setErrors(fromApi);
      } else {
        setGeneralError(generalErrorMessage(error));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="mx-auto flex max-w-[480px] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <header className="flex flex-col gap-[var(--gg-space-2)]">
        <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">建立帳號</h1>
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          舊平台的會員與訂單不會自動搬過來，請重新註冊一次。
          <strong className="text-fg">舊訂單請到原平台查詢，查詢期限請見公告。</strong>
        </p>
      </header>

      <Card>
        <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-[var(--gg-space-4)]">
          <Field label="手機號碼" htmlFor="register-phone" required error={errors.phoneNumber} hint="這是登入帳號，註冊後不能自行更改">
            <Input
              id="register-phone"
              type="tel"
              inputMode="numeric"
              autoComplete="tel"
              invalid={Boolean(errors.phoneNumber)}
              value={values.phoneNumber}
              onChange={(e) => setField('phoneNumber', e.target.value)}
              placeholder="0912345678"
            />
          </Field>

          <Field label="密碼" htmlFor="register-password" required error={errors.password} hint="8～128 碼">
            <Input
              id="register-password"
              type="password"
              autoComplete="new-password"
              invalid={Boolean(errors.password)}
              value={values.password}
              onChange={(e) => setField('password', e.target.value)}
            />
          </Field>

          <Field label="顯示名稱" htmlFor="register-display-name" required error={errors.displayName}>
            <Input
              id="register-display-name"
              autoComplete="nickname"
              invalid={Boolean(errors.displayName)}
              value={values.displayName}
              onChange={(e) => setField('displayName', e.target.value)}
              placeholder="王小美"
            />
          </Field>

          <Field label="Email" htmlFor="register-email" error={errors.email} hint="選填">
            <Input
              id="register-email"
              type="email"
              autoComplete="email"
              invalid={Boolean(errors.email)}
              value={values.email}
              onChange={(e) => setField('email', e.target.value)}
            />
          </Field>

          <Field label="推薦碼" htmlFor="register-referral-code" error={errors.referralCode} hint="選填">
            <Input
              id="register-referral-code"
              invalid={Boolean(errors.referralCode)}
              value={values.referralCode}
              onChange={(e) => setField('referralCode', e.target.value)}
            />
          </Field>

          {generalError && (
            <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
              {generalError}
            </p>
          )}

          <Button type="submit" variant="primary" size="lg" loading={submitting} fullWidth>
            建立帳號
          </Button>
        </form>
      </Card>

      <p className="text-center text-[length:var(--gg-text-sm)] text-fg-muted">
        已經有帳號了？{' '}
        <Link href="/login" className="font-bold text-primary-text">
          直接登入
        </Link>
      </p>
    </main>
  );
}
