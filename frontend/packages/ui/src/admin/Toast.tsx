'use client';

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { CheckCircleIcon, CloseIcon, ErrorIcon } from './icons';

export type ToastVariant = 'success' | 'error';

export interface ToastItem {
  readonly id: string;
  readonly variant: ToastVariant;
  readonly message: string;
}

interface ToastContextValue {
  show: (variant: ToastVariant, message: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

const AUTO_DISMISS_MS = 4000;

export function ToastProvider({ children }: { readonly children: ReactNode }) {
  const [toasts, setToasts] = useState<readonly ToastItem[]>([]);

  const dismiss = useCallback((id: string) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const show = useCallback(
    (variant: ToastVariant, message: string) => {
      const id = crypto.randomUUID();
      setToasts((current) => [...current, { id, variant, message }]);
      setTimeout(() => dismiss(id), AUTO_DISMISS_MS);
    },
    [dismiss],
  );

  const value = useMemo(() => ({ show }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div
        className="fixed bottom-4 right-4 flex flex-col gap-2"
        style={{ zIndex: 'var(--ga-z-popover)' }}
      >
        {toasts.map((toast) => (
          <Toast key={toast.id} toast={toast} onDismiss={() => dismiss(toast.id)} />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

/** 成功／失敗兩種 toast。要在 `ToastProvider` 底下才能用。 */
export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast 必須在 <ToastProvider> 底下使用');
  }
  return context;
}

function Toast({ toast, onDismiss }: { readonly toast: ToastItem; readonly onDismiss: () => void }) {
  const isSuccess = toast.variant === 'success';
  return (
    <div
      role="status"
      className={`flex items-start gap-2 rounded-card border px-4 py-3 text-sm shadow-popover ${
        isSuccess
          ? 'border-success/30 bg-success-subtle text-success'
          : 'border-danger/30 bg-danger-subtle text-danger'
      }`}
    >
      {isSuccess ? <CheckCircleIcon className="mt-0.5 h-4 w-4 shrink-0" /> : <ErrorIcon className="mt-0.5 h-4 w-4 shrink-0" />}
      <p className="flex-1 font-medium">{toast.message}</p>
      <button type="button" onClick={onDismiss} aria-label="關閉通知" className="shrink-0 opacity-70 hover:opacity-100">
        <CloseIcon className="h-4 w-4" />
      </button>
    </div>
  );
}
