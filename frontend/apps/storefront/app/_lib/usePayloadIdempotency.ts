'use client';

import { createPayloadIdempotencyScope, type PayloadIdempotencyScope } from '@greygray/api-client';
import { useRef } from 'react';

/** 每個 mounted form／button flow 持有自己的 payload-aware 冪等鍵。 */
export function usePayloadIdempotency(): PayloadIdempotencyScope {
  const scopeRef = useRef<PayloadIdempotencyScope | null>(null);
  scopeRef.current ??= createPayloadIdempotencyScope();
  return scopeRef.current;
}
