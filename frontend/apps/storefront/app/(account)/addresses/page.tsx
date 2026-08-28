'use client';

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { Badge, Button, Card, Dialog, EmptyState, ErrorState, Skeleton } from '@greygray/ui';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { usePayloadIdempotency } from '../../_lib/usePayloadIdempotency';
import { AddressForm } from '../_components/AddressForm';
import { ConfirmDialog } from '../_components/ConfirmDialog';
import {
  ADDRESS_INITIAL_VALUES,
  type AddressFieldErrors,
  type AddressFormValues,
  addressValuesFrom,
  toAddressInput,
  validateAddressForm,
} from '../_lib/addressSchema';
import { isUnauthorized } from '../_lib/authRedirect';
import { fieldErrorsFrom, generalErrorMessage, traceIdOf } from '../_lib/formErrors';

type ShippingAddress = components['schemas']['ShippingAddress'];

export default function AddressesPage() {
  const router = useRouter();
  const [addresses, setAddresses] = useState<ShippingAddress[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [editing, setEditing] = useState<ShippingAddress | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [formValues, setFormValues] = useState<AddressFormValues>(ADDRESS_INITIAL_VALUES);
  const [formErrors, setFormErrors] = useState<AddressFieldErrors>({});
  const [formGeneralError, setFormGeneralError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const idempotency = usePayloadIdempotency();

  const [deleteTarget, setDeleteTarget] = useState<ShippingAddress | null>(null);
  const [deleting, setDeleting] = useState(false);

  const load = () => {
    setLoading(true);
    setError(null);
    storefrontApi
      .listAddresses(browserApi())
      .then(setAddresses)
      .catch((caught: unknown) => {
        if (isUnauthorized(caught)) {
          router.replace('/login');
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

  function openCreate() {
    setEditing(null);
    setFormValues(ADDRESS_INITIAL_VALUES);
    setFormErrors({});
    setFormGeneralError(null);
    setFormOpen(true);
  }

  function openEdit(address: ShippingAddress) {
    setEditing(address);
    setFormValues(addressValuesFrom(address));
    setFormErrors({});
    setFormGeneralError(null);
    setFormOpen(true);
  }

  async function handleSubmit() {
    const clientErrors = validateAddressForm(formValues);
    if (Object.keys(clientErrors).length > 0) {
      setFormErrors(clientErrors);
      return;
    }

    setSaving(true);
    setFormGeneralError(null);
    try {
      const input = toAddressInput(formValues);
      const payload = editing
        ? { action: 'update-address', addressId: editing.id, input }
        : { action: 'create-address', input };
      const options = { idempotencyKey: idempotency.current(payload) };
      if (editing) {
        const updated = await storefrontApi.updateAddress(browserApi(), editing.id, input, options);
        setAddresses((prev) => prev.map((a) => (a.id === updated.id ? updated : a)));
      } else {
        const created = await storefrontApi.createAddress(browserApi(), input, options);
        setAddresses((prev) => [...prev, created]);
      }
      idempotency.complete();
      setFormOpen(false);
    } catch (caught) {
      if (isUnauthorized(caught)) {
        router.replace('/login');
        return;
      }
      const fromApi = fieldErrorsFrom(caught);
      if (Object.keys(fromApi).length > 0) {
        setFormErrors(fromApi);
      } else {
        setFormGeneralError(generalErrorMessage(caught));
      }
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      const payload = { action: 'delete-address', addressId: deleteTarget.id };
      await storefrontApi.deleteAddress(browserApi(), deleteTarget.id, {
        idempotencyKey: idempotency.current(payload),
      });
      idempotency.complete();
      setAddresses((prev) => prev.filter((a) => a.id !== deleteTarget.id));
      setDeleteTarget(null);
    } catch (caught) {
      if (isUnauthorized(caught)) {
        router.replace('/login');
        return;
      }
      // 刪除失敗留在原地讓使用者看得到，不用另外開錯誤畫面。
      setError(caught);
    } finally {
      setDeleting(false);
    }
  }

  return (
    <main className="mx-auto flex max-w-[640px] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <header className="flex items-center justify-between gap-[var(--gg-space-3)]">
        <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">收件地址</h1>
        <Button size="sm" onClick={openCreate}>
          新增地址
        </Button>
      </header>

      {loading && (
        <div className="flex flex-col gap-[var(--gg-space-3)]">
          {[0, 1].map((i) => (
            <Skeleton key={i} variant="block" className="h-[120px] w-full" />
          ))}
        </div>
      )}

      {!loading && error != null && (
        <Card padding="none">
          <ErrorState title={generalErrorMessage(error)} traceId={traceIdOf(error)} onRetry={load} />
        </Card>
      )}

      {!loading && !error && addresses.length === 0 && (
        <Card padding="none">
          <EmptyState
            title="還沒有收件地址"
            description="新增一筆地址，結帳時就能直接選。"
            action={
              <Button size="sm" onClick={openCreate}>
                新增地址
              </Button>
            }
          />
        </Card>
      )}

      {!loading && !error && addresses.length > 0 && (
        <div className="flex flex-col gap-[var(--gg-space-3)]">
          {addresses.map((address) => (
            <Card key={address.id} className="flex flex-col gap-[var(--gg-space-2)]">
              <div className="flex items-center gap-[var(--gg-space-2)]">
                <p className="font-bold text-fg">{address.recipientName}</p>
                {address.isDefault && <Badge variant="Default" label="預設" />}
              </div>
              <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{address.phoneNumber}</p>
              <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
                {address.postalCode} {address.city}
                {address.district}
                {address.streetAddress}
              </p>
              <div className="flex gap-[var(--gg-space-3)]">
                <Button variant="secondary" size="sm" onClick={() => openEdit(address)}>
                  編輯
                </Button>
                <Button variant="ghost" size="sm" onClick={() => setDeleteTarget(address)}>
                  刪除
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Dialog
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? '編輯收件地址' : '新增收件地址'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)} disabled={saving}>
              取消
            </Button>
            <Button variant="primary" onClick={handleSubmit} loading={saving}>
              儲存
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-[var(--gg-space-3)]">
          {formGeneralError && (
            <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
              {formGeneralError}
            </p>
          )}
          <AddressForm values={formValues} errors={formErrors} onChange={setFormValues} />
        </div>
      </Dialog>

      <ConfirmDialog
        open={deleteTarget !== null}
        title="刪除這筆地址？"
        description={deleteTarget ? `刪除「${deleteTarget.recipientName}」的收件地址，之後結帳就不會再看到它。` : ''}
        confirmLabel="確定刪除"
        danger
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </main>
  );
}
