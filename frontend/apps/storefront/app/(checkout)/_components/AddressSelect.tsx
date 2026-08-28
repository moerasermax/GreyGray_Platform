'use client';

import Link from 'next/link';
import { Field, Select, Skeleton } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export interface AddressSelectProps {
  addresses: S['ShippingAddress'][];
  loading: boolean;
  value: string | null;
  onChange: (addressId: string) => void;
}

/**
 * 只負責「選一個既有地址」。**新增／編輯地址是 FE-5 的地盤**
 * （`apps/storefront/app/(account)/**`），這裡不重做一份，沒地址時導過去就好。
 */
export function AddressSelect({ addresses, loading, value, onChange }: AddressSelectProps) {
  if (loading) {
    return <Skeleton variant="block" className="h-[var(--gg-space-7)] w-full" />;
  }

  if (addresses.length === 0) {
    return (
      <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
        還沒有收件地址。請先到{' '}
        <Link href="/addresses" className="text-primary-text underline">
          會員中心新增地址
        </Link>
        。
      </p>
    );
  }

  return (
    <Field label="收件地址" htmlFor="checkout-address" required>
      <Select id="checkout-address" value={value ?? ''} onChange={(e) => onChange(e.target.value)}>
        <option value="" disabled>
          請選擇收件地址
        </option>
        {addresses.map((address) => (
          <option key={address.id} value={address.id}>
            {address.recipientName}・{address.city}{address.district}{address.streetAddress}
            {address.isDefault ? '（預設）' : ''}
          </option>
        ))}
      </Select>
    </Field>
  );
}
