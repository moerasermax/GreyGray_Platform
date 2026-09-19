using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Modules.CustomerService.Infra;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.CustomerService.Tests;

/// <summary>
/// 對真 PostgreSQL 驗兩件事：① repository 的排序／游標比較真的翻得成 SQL（不是只在
/// fake repository 上跑過）；② migration 0022 的 CHECK constraint 真的擋得住違規資料，
/// 不是只在 ORM 模型裡好看——這是知識庫記過的假綠燈（「用了真 DB 不代表約束來自 migration」），
/// 所以第二個測試刻意繞過 EF 驗證，直接下未過驗證的 SQL INSERT。
/// </summary>
public sealed class CustomerServiceMigrationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "游標分頁：TicketId 比較翻得成 SQL，PlacedAt 相同時用 Id 遞減 tie-break")]
    public async Task Repository_pagination_translates_to_sql_and_breaks_ties_by_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateDbContextAsync("cs_pagination", cancellationToken);
        var repository = new TicketRepository(dbContext);

        // 三筆 CreatedAt 完全相同，逼查詢真的用到 Id 這個第二鍵。
        var small = SeedTicket(dbContext, new Guid("11111111-1111-1111-1111-111111111111"));
        var middle = SeedTicket(dbContext, new Guid("22222222-2222-2222-2222-222222222222"));
        var large = SeedTicket(dbContext, new Guid("33333333-3333-3333-3333-333333333333"));
        await dbContext.SaveChangesAsync(cancellationToken);

        var first = await repository.ListAsync(TenantId.Default, null, null, 2, cancellationToken);
        first.Items.Select(ticket => ticket.Id).ShouldBe([large.Id, middle.Id]);
        first.HasNext.ShouldBeTrue();

        var second = await repository.ListAsync(
            TenantId.Default, null, first.Items[^1].Id, 2, cancellationToken);
        second.Items.Select(ticket => ticket.Id).ShouldBe([small.Id]);
        second.HasNext.ShouldBeFalse();
    }

    [Fact(DisplayName = "migration 0022：ticket_contact_required 擋住 email 與 phone 都是 null 的資料列")]
    public async Task Migration_check_constraint_rejects_missing_contact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("cs_constraint_contact", cancellationToken);
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            $"""
            INSERT INTO customer_service.ticket
                (id, tenant_id, status, contact_email, contact_phone, created_at)
            VALUES
                ('{Guid.NewGuid():N}', '{TenantId.Default.Value:N}', 0, NULL, NULL, now());
            """,
            cancellationToken));
        exception.SqlState.ShouldBe("23514");
        exception.ConstraintName.ShouldBe("ticket_contact_required");
    }

    [Fact(DisplayName = "migration 0022：ticket_resolution_consistent 擋住 status=open 但填了 resolved_at 的資料列")]
    public async Task Migration_check_constraint_rejects_inconsistent_resolution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("cs_constraint_resolution", cancellationToken);
        await ApplyMigrationsAsync(connectionString, cancellationToken);

        var exception = await Should.ThrowAsync<PostgresException>(() => ExecuteSqlAsync(
            connectionString,
            $"""
            INSERT INTO customer_service.ticket
                (id, tenant_id, status, contact_email, created_at, resolved_at)
            VALUES
                ('{Guid.NewGuid():N}', '{TenantId.Default.Value:N}', 0, 'a@b.com', now(), now());
            """,
            cancellationToken));
        exception.SqlState.ShouldBe("23514");
        exception.ConstraintName.ShouldBe("ticket_resolution_consistent");
    }

    [Fact(DisplayName = "migration 0022 可重放兩次")]
    public async Task Migration_is_replayable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync("cs_replay", cancellationToken);

        await ApplyMigrationsAsync(connectionString, cancellationToken);
        await Should.NotThrowAsync(() => ApplyMigrationsAsync(connectionString, cancellationToken));
    }

    /// <summary>
    /// 用 <see cref="Ticket.Reconstruct"/> 塞入刻意寫死、只差數值大小的 ID——
    /// 跟 <c>OrderingAdminListSortPostgresTests</c> 同一種手法，用來逼 tie-break
    /// 真的用到第二排序鍵。不能用 <see cref="Ticket.Create"/> 再事後改 <c>Id</c>：
    /// <see cref="TicketMessage"/> 的 <c>TicketId</c> 會維持 <c>Create</c> 當下產生的值，
    /// 跟改過的 <c>Ticket.Id</c> 對不起來。
    /// </summary>
    private static Ticket SeedTicket(CustomerServiceDbContext dbContext, Guid id)
    {
        var ticketId = new TicketId(id);
        var ticket = Ticket.Reconstruct(
            ticketId,
            TenantId.Default,
            SupportTicketStatus.Open,
            "a@b.com",
            null,
            null,
            null,
            Now,
            null,
            null,
            null,
            [TicketMessage.Reconstruct(Guid.CreateVersion7(), ticketId, $"問題 {id}", [], Now)]);
        dbContext.Tickets.Add(ticket);
        return ticket;
    }

    private async Task<CustomerServiceDbContext> CreateDbContextAsync(
        string prefix, CancellationToken cancellationToken)
    {
        var connectionString = await CreateDatabaseAsync(prefix, cancellationToken);
        var dbContext = new CustomerServiceDbContext(
            new DbContextOptionsBuilder<CustomerServiceDbContext>().UseNpgsql(connectionString).Options);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        return dbContext;
    }

    private async Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken)
    {
        var databaseName = $"{prefix}_{Guid.NewGuid():N}";
        await ExecuteSqlAsync(_postgres.GetConnectionString(), $"CREATE DATABASE \"{databaseName}\";", cancellationToken);
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = databaseName }
            .ConnectionString;
    }

    private static async Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var migrationRoot = Path.Combine(FindRepositoryRoot(), "db", "migrations");
        foreach (var fileName in new[] { "0001_schemas_and_roles.sql", "0022_customer_service_ticket.sql" })
        {
            await ExecuteMigrationFileAsync(connectionString, Path.Combine(migrationRoot, fileName), cancellationToken);
        }
    }

    private static Task ExecuteMigrationFileAsync(
        string connectionString, string path, CancellationToken cancellationToken)
    {
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        return ExecuteSqlAsync(connectionString, sql, cancellationToken);
    }

    private static async Task ExecuteSqlAsync(
        string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Cannot find the repository root.");
    }
}
