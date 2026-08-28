import type { components } from '@greygray/api-client/storefront';

type PaymentInitiation = components['schemas']['PaymentInitiation'];

/**
 * 把 `PaymentInitiation.fields` **原封不動** POST 到 `action`。
 * 不自己組簽章、不改任何欄位的值、不少送欄位（`docs/06-前端工作包.md` FE-4 做法要點）。
 */
export function submitPaymentForm(initiation: PaymentInitiation): void {
  const form = document.createElement('form');
  form.method = initiation.method;
  form.action = initiation.action;
  form.style.display = 'none';

  for (const [name, value] of Object.entries(initiation.fields)) {
    const input = document.createElement('input');
    input.type = 'hidden';
    input.name = name;
    input.value = value;
    form.appendChild(input);
  }

  document.body.appendChild(form);
  form.submit();
}
