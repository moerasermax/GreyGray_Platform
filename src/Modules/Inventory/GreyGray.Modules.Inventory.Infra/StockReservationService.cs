using GreyGray.Modules.Inventory.Contracts;
using GreyGray.Modules.Inventory.Core;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GreyGray.Modules.Inventory.Infra;

internal sealed class StockReservationService(
    InventoryDbContext dbContext,
    IEventPublisher eventPublisher,
    IClock clock,
    ICorrelationContext correlationContext) : IStockReservation
{
    private const short ActiveStatus = 0;
    private const short ReleasedStatus = 1;

    public async Task<Result<ReservationId>> ReserveAsync(
        string reservationKey,
        IReadOnlyList<(Modules.Catalog.Contracts.SkuId SkuId, int Quantity)> lines,
        CancellationToken cancellationToken)
    {
        var planResult = StockReservationPlan.Create(reservationKey, lines);
        if (planResult.IsFailure)
        {
            return Result<ReservationId>.Failure(planResult.Error);
        }

        IDbContextTransaction? ownedTransaction = null;
        try
        {
            ownedTransaction = await BeginIfNeededAsync(cancellationToken);
            var transaction = CurrentTransaction();
            var tenantId = correlationContext.TenantId;
            var reservationId = ReservationId.New();
            var inserted = await InsertReservationAsync(
                reservationId,
                tenantId,
                planResult.Value.ReservationKey,
                transaction,
                cancellationToken);

            if (!inserted)
            {
                var existing = await GetByKeyAsync(
                    tenantId,
                    planResult.Value.ReservationKey,
                    transaction,
                    cancellationToken);
                await CommitIfOwnedAsync(ownedTransaction, cancellationToken);
                return existing is null
                    ? Result<ReservationId>.Failure(
                        "inventory.reservation-replay-missing",
                        "冪等保留已存在但無法讀回，請稍後重試。")
                    : new ReservationId(existing.Value.Id);
            }

            foreach (var line in planResult.Value.Lines)
            {
                var allocations = await LockAndPlanAllocationsAsync(
                    tenantId,
                    line,
                    transaction,
                    cancellationToken);
                if (allocations is null)
                {
                    await RollbackIfOwnedAsync(ownedTransaction);
                    return Result<ReservationId>.Failure(
                        "inventory.insufficient-stock",
                        $"SKU {line.SkuId} 的可用庫存不足，無法保留 {line.Quantity} 件。");
                }

                foreach (var allocation in allocations)
                {
                    var updated = await IncreaseReservedAsync(
                        tenantId,
                        allocation,
                        transaction,
                        cancellationToken);
                    if (updated != 1)
                    {
                        throw new InvalidOperationException(
                            $"已鎖定 lot {allocation.LotId:N} 但原子保留失敗，交易已中止。");
                    }

                    await InsertAllocationAsync(
                        reservationId,
                        tenantId,
                        line.SkuId.Value,
                        allocation,
                        transaction,
                        cancellationToken);
                }

                await eventPublisher.PublishAsync(
                    new StockReserved(
                        Guid.CreateVersion7(),
                        clock.UtcNow,
                        tenantId,
                        reservationId,
                        line.SkuId,
                        line.Quantity),
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfOwnedAsync(ownedTransaction, cancellationToken);
            return reservationId;
        }
        catch
        {
            await RollbackIfOwnedAsync(ownedTransaction);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }

    public Task<Result> ReleaseAsync(
        ReservationId id,
        CancellationToken cancellationToken) =>
        ReleaseAsync(id, reservationKey: null, missingIsSuccess: false, cancellationToken);

    internal Task<Result> ReleaseByKeyAsync(
        string reservationKey,
        CancellationToken cancellationToken) =>
        ReleaseAsync(id: null, reservationKey, missingIsSuccess: true, cancellationToken);

    private async Task<Result> ReleaseAsync(
        ReservationId? id,
        string? reservationKey,
        bool missingIsSuccess,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? ownedTransaction = null;
        try
        {
            ownedTransaction = await BeginIfNeededAsync(cancellationToken);
            var transaction = CurrentTransaction();
            var tenantId = correlationContext.TenantId;
            var reservation = await LockReservationAsync(
                tenantId,
                id,
                reservationKey,
                transaction,
                cancellationToken);

            if (reservation is null)
            {
                await CommitIfOwnedAsync(ownedTransaction, cancellationToken);
                return missingIsSuccess
                    ? Result.Success()
                    : Result.Failure("inventory.reservation-not-found", "找不到庫存保留紀錄。");
            }

            if (reservation.Value.Status == ReleasedStatus)
            {
                await CommitIfOwnedAsync(ownedTransaction, cancellationToken);
                return Result.Success();
            }

            var allocationCount = await CountAllocationsAsync(
                tenantId,
                reservation.Value.Id,
                transaction,
                cancellationToken);
            var releasedLots = await DecreaseReservedAsync(
                tenantId,
                reservation.Value.Id,
                transaction,
                cancellationToken);
            if (releasedLots != allocationCount)
            {
                throw new InvalidOperationException(
                    $"reservation {reservation.Value.Id:N} 的 allocation 與 lot 保留量不一致，拒絕部分釋放。");
            }

            var markedReleased = await MarkReleasedAsync(
                tenantId,
                reservation.Value.Id,
                transaction,
                cancellationToken);
            if (markedReleased != 1)
            {
                throw new InvalidOperationException(
                    $"reservation {reservation.Value.Id:N} 已鎖定但狀態更新失敗。");
            }

            await eventPublisher.PublishAsync(
                new StockReleased(
                    Guid.CreateVersion7(),
                    clock.UtcNow,
                    tenantId,
                    new ReservationId(reservation.Value.Id)),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfOwnedAsync(ownedTransaction, cancellationToken);
            return Result.Success();
        }
        catch
        {
            await RollbackIfOwnedAsync(ownedTransaction);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }

    private async Task<IDbContextTransaction?> BeginIfNeededAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private NpgsqlTransaction CurrentTransaction() =>
        (NpgsqlTransaction)(dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("庫存異動必須在資料庫交易內執行。"))
        .GetDbTransaction();

    private async Task CommitIfOwnedAsync(
        IDbContextTransaction? ownedTransaction,
        CancellationToken cancellationToken)
    {
        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }
    }

    private async Task RollbackIfOwnedAsync(IDbContextTransaction? ownedTransaction)
    {
        if (ownedTransaction is not null)
        {
            await ownedTransaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
        }
    }

    private NpgsqlCommand Command(string sql, NpgsqlTransaction transaction) =>
        new(sql, (NpgsqlConnection)dbContext.Database.GetDbConnection(), transaction);

    private async Task<bool> InsertReservationAsync(
        ReservationId reservationId,
        TenantId tenantId,
        string reservationKey,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            INSERT INTO inventory.reservation (
                id, tenant_id, reservation_key, status, created_at)
            VALUES (@id, @tenant_id, @reservation_key, 0, @created_at)
            ON CONFLICT (tenant_id, reservation_key) DO NOTHING;
            """, transaction);
        command.Parameters.AddWithValue("id", reservationId.Value);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("reservation_key", reservationKey);
        command.Parameters.AddWithValue("created_at", clock.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<(Guid Id, short Status)?> GetByKeyAsync(
        TenantId tenantId,
        string reservationKey,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            SELECT id, status
            FROM inventory.reservation
            WHERE tenant_id = @tenant_id AND reservation_key = @reservation_key;
            """, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("reservation_key", reservationKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetInt16(1))
            : null;
    }

    private async Task<IReadOnlyList<LotAllocation>?> LockAndPlanAllocationsAsync(
        TenantId tenantId,
        StockReservationLine line,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var lots = new List<(Guid Id, int Available)>();
        await using (var command = Command("""
            SELECT id, quantity_available
            FROM inventory.lot
            WHERE tenant_id = @tenant_id
              AND sku_id = @sku_id
              AND quantity_available > 0
            ORDER BY id
            FOR UPDATE;
            """, transaction))
        {
            command.Parameters.AddWithValue("tenant_id", tenantId.Value);
            command.Parameters.AddWithValue("sku_id", line.SkuId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lots.Add((reader.GetGuid(0), reader.GetInt32(1)));
            }
        }

        var remaining = line.Quantity;
        var allocations = new List<LotAllocation>();
        foreach (var lot in lots)
        {
            var quantity = Math.Min(remaining, lot.Available);
            if (quantity > 0)
            {
                allocations.Add(new LotAllocation(lot.Id, quantity));
                remaining -= quantity;
            }

            if (remaining == 0)
            {
                return allocations;
            }
        }

        return null;
    }

    private async Task<int> IncreaseReservedAsync(
        TenantId tenantId,
        LotAllocation allocation,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            UPDATE inventory.lot
            SET quantity_reserved = quantity_reserved + @quantity
            WHERE tenant_id = @tenant_id
              AND id = @lot_id
              AND quantity_available >= @quantity;
            """, transaction);
        command.Parameters.AddWithValue("quantity", allocation.Quantity);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("lot_id", allocation.LotId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertAllocationAsync(
        ReservationId reservationId,
        TenantId tenantId,
        Guid skuId,
        LotAllocation allocation,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            INSERT INTO inventory.reservation_allocation (
                reservation_id, tenant_id, lot_id, sku_id, quantity)
            VALUES (@reservation_id, @tenant_id, @lot_id, @sku_id, @quantity);
            """, transaction);
        command.Parameters.AddWithValue("reservation_id", reservationId.Value);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("lot_id", allocation.LotId);
        command.Parameters.AddWithValue("sku_id", skuId);
        command.Parameters.AddWithValue("quantity", allocation.Quantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(Guid Id, short Status)?> LockReservationAsync(
        TenantId tenantId,
        ReservationId? id,
        string? reservationKey,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var predicate = id is not null ? "id = @value" : "reservation_key = @value";
        await using var command = Command($"""
            SELECT id, status
            FROM inventory.reservation
            WHERE tenant_id = @tenant_id AND {predicate}
            FOR UPDATE;
            """, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue(
            "value",
            id is not null ? id.Value.Value : reservationKey!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetInt16(1))
            : null;
    }

    private async Task<int> CountAllocationsAsync(
        TenantId tenantId,
        Guid reservationId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            SELECT count(*)::integer
            FROM inventory.reservation_allocation
            WHERE tenant_id = @tenant_id AND reservation_id = @reservation_id;
            """, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("reservation_id", reservationId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("無法讀取 reservation allocation 數量。"));
    }

    private async Task<int> DecreaseReservedAsync(
        TenantId tenantId,
        Guid reservationId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            UPDATE inventory.lot AS lot
            SET quantity_reserved = lot.quantity_reserved - allocation.quantity
            FROM inventory.reservation_allocation AS allocation
            WHERE allocation.tenant_id = @tenant_id
              AND allocation.reservation_id = @reservation_id
              AND lot.tenant_id = allocation.tenant_id
              AND lot.id = allocation.lot_id
              AND lot.sku_id = allocation.sku_id
              AND lot.quantity_reserved >= allocation.quantity;
            """, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("reservation_id", reservationId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> MarkReleasedAsync(
        TenantId tenantId,
        Guid reservationId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
            UPDATE inventory.reservation
            SET status = 1, released_at = @released_at
            WHERE tenant_id = @tenant_id AND id = @reservation_id AND status = 0;
            """, transaction);
        command.Parameters.AddWithValue("released_at", clock.UtcNow);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("reservation_id", reservationId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record LotAllocation(Guid LotId, int Quantity);
}
