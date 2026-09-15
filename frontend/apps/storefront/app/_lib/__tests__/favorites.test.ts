import { ApiError, createPayloadIdempotencyScope } from '@greygray/api-client';
import { describe, expect, it, vi } from 'vitest';
import { executeFavoriteToggle, type FavoriteMutationPayload } from '../favorites';

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

function apiError(status: number) {
  return new ApiError({
    type: `https://greygray.tw/errors/test-${status}`,
    title: status === 401 ? '請先登入' : '更新失敗',
    status,
    code: status === 401 ? 'identity.unauthorized' : 'catalog.favorite-failed',
  });
}

function setup(initialFavorited = false) {
  let current = initialFavorited;
  const pendingRef = { current: false };
  const mutate = vi.fn<(payload: FavoriteMutationPayload, key: string) => Promise<void>>(
    async () => {},
  );
  const changes: boolean[] = [];
  const onUnauthorized = vi.fn();
  const onError = vi.fn();

  const run = () => executeFavoriteToggle({
    productId: 'product-1',
    currentFavorited: current,
    pendingRef,
    idempotency: createPayloadIdempotencyScope(),
    mutate,
    onChange: (next) => {
      current = next;
      changes.push(next);
    },
    onUnauthorized,
    onError,
  });

  return { run, mutate, changes, onUnauthorized, onError, pendingRef, current: () => current };
}

describe('executeFavoriteToggle', () => {
  it('先樂觀變色；同一個 tick 連按兩次只送一個請求', async () => {
    const gate = deferred();
    const state = setup();
    state.mutate.mockImplementation(() => gate.promise);

    const first = state.run();
    const second = state.run();

    expect(state.changes).toEqual([true]);
    expect(state.mutate).toHaveBeenCalledTimes(1);
    expect(state.pendingRef.current).toBe(true);
    gate.resolve();
    await Promise.all([first, second]);
    expect(state.pendingRef.current).toBe(false);
  });

  it('非 401 失敗會還原原狀並顯示錯誤', async () => {
    const state = setup(false);
    state.mutate.mockRejectedValue(apiError(500));

    await state.run();

    expect(state.changes).toEqual([true, false]);
    expect(state.current()).toBe(false);
    expect(state.onError).toHaveBeenCalledWith('更新失敗');
    expect(state.onUnauthorized).not.toHaveBeenCalled();
  });

  it('401 先還原成未收藏，再導去登入，不顯示一般錯誤', async () => {
    const events: string[] = [];
    let current = true;
    await executeFavoriteToggle({
      productId: 'product-1',
      currentFavorited: true,
      pendingRef: { current: false },
      idempotency: createPayloadIdempotencyScope(),
      mutate: async () => { throw apiError(401); },
      onChange: (next) => {
        current = next;
        events.push(`change:${next}`);
      },
      onUnauthorized: () => events.push('redirect'),
      onError: () => events.push('error'),
    });

    expect(current).toBe(false);
    expect(events).toEqual(['change:false', 'change:false', 'redirect']);
  });

  it('取消後完成，再按一次會送加入，狀態可回到已收藏', async () => {
    const state = setup(true);
    await state.run();
    await state.run();

    expect(state.mutate.mock.calls.map(([payload]) => payload.desiredState)).toEqual([false, true]);
    expect(state.current()).toBe(true);
  });
});
