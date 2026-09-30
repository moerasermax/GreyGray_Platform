'use client';

import { useCallback, useEffect, useRef, useState, type Ref } from 'react';
import { IconButton } from './IconButton';
import { IconEye, IconEyeOff } from './icons';
import { Input, type InputProps } from './Input';
import { cn } from './internal/cn';

const PASSWORD_SHOW_LABEL = '顯示密碼';
const PASSWORD_HIDE_LABEL = '隱藏密碼';

export interface PasswordInputProps
  extends Omit<InputProps, 'type' | 'id' | 'autoComplete'> {
  id: string;
  autoComplete: 'current-password' | 'new-password';
}

export interface PasswordToggleTarget {
  type: string;
  selectionStart: number | null;
  selectionEnd: number | null;
  setSelectionRange(start: number, end: number): void;
  focus(): void;
}

export function passwordInputState(visible: boolean) {
  return {
    inputType: visible ? ('text' as const) : ('password' as const),
    toggleLabel: visible ? PASSWORD_HIDE_LABEL : PASSWORD_SHOW_LABEL,
  };
}

export function passwordToggleInput(input: PasswordToggleTarget): boolean {
  const selectionStart = input.selectionStart;
  const selectionEnd = input.selectionEnd;
  const visible = input.type === 'password';

  input.type = visible ? 'text' : 'password';
  if (selectionStart !== null && selectionEnd !== null) {
    input.setSelectionRange(selectionStart, selectionEnd);
  }
  input.focus();

  return visible;
}

export function passwordMaskInput(input: Pick<PasswordToggleTarget, 'type'>): void {
  input.type = 'password';
}

function passwordAssignRef(
  ref: Ref<HTMLInputElement> | undefined,
  input: HTMLInputElement | null,
): void {
  if (typeof ref === 'function') {
    ref(input);
  } else if (ref) {
    ref.current = input;
  }
}

export function PasswordInput({
  id,
  autoComplete,
  className,
  ref,
  ...rest
}: PasswordInputProps) {
  const [visible, setVisible] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const state = passwordInputState(visible);
  const setInputRef = useCallback(
    (input: HTMLInputElement | null) => {
      inputRef.current = input;
      passwordAssignRef(ref, input);
    },
    [ref],
  );

  useEffect(() => {
    const input = inputRef.current;
    const form = input?.form;
    if (!input || !form) return;

    const handleSubmit = () => {
      passwordMaskInput(input);
      setVisible(false);
    };
    form.addEventListener('submit', handleSubmit, true);
    return () => form.removeEventListener('submit', handleSubmit, true);
  }, []);

  function handleToggle() {
    const input = inputRef.current;
    if (!input) return;

    const nextVisible = passwordToggleInput(input);
    setVisible(nextVisible);
  }

  return (
    <div className="relative">
      <Input
        {...rest}
        ref={setInputRef}
        id={id}
        type={state.inputType}
        autoComplete={autoComplete}
        className={cn('pr-[var(--gg-touch-min)] [&::-ms-reveal]:hidden', className)}
      />
      <IconButton
        type="button"
        aria-label={state.toggleLabel}
        aria-controls={id}
        icon={visible ? <IconEyeOff /> : <IconEye />}
        className="absolute inset-y-0 right-0"
        onClick={handleToggle}
      />
    </div>
  );
}
