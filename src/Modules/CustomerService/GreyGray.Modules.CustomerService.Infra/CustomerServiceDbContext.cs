using GreyGray.Modules.CustomerService.Contracts;
using GreyGray.Modules.CustomerService.Core;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace GreyGray.Modules.CustomerService.Infra;

internal sealed class CustomerServiceDbContext(DbContextOptions<CustomerServiceDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("customer_service");
        ConfigureTicket(modelBuilder);
        ConfigureTicketMessage(modelBuilder);
        modelBuilder.AddPlatformTables();
    }

    private static void ConfigureTicket(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Ticket>();
        entity.ToTable("ticket", "customer_service");
        entity.HasKey(ticket => ticket.Id);
        entity.Property(ticket => ticket.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TicketId(value))
            .ValueGeneratedNever();
        entity.Property(ticket => ticket.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();
        entity.Property(ticket => ticket.Status)
            .HasColumnName("status")
            .HasConversion<short>()
            .IsRequired();
        entity.Property(ticket => ticket.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(200);
        entity.Property(ticket => ticket.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(20);
        entity.Property(ticket => ticket.OrderId)
            .HasColumnName("order_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new TicketOrderId(value.Value) : null);
        entity.Property(ticket => ticket.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new CustomerId(value.Value) : null);
        entity.Property(ticket => ticket.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.Property(ticket => ticket.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(ticket => ticket.ResolvedBy)
            .HasColumnName("resolved_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new StaffId(value.Value) : null);
        entity.Property(ticket => ticket.StaffNote)
            .HasColumnName("staff_note");

        entity.HasMany(ticket => ticket.Messages)
            .WithOne()
            .HasForeignKey(message => message.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(ticket => ticket.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(ticket => new { ticket.TenantId, ticket.Status, ticket.CreatedAt, ticket.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("ix_ticket_tenant_status_created");
    }

    private static void ConfigureTicketMessage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TicketMessage>();
        entity.ToTable("ticket_message", "customer_service");
        entity.HasKey(message => message.Id);
        entity.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        entity.Property(message => message.TicketId)
            .HasColumnName("ticket_id")
            .HasConversion(id => id.Value, value => new TicketId(value))
            .IsRequired();
        entity.Property(message => message.Body)
            .HasColumnName("body")
            .IsRequired();
        entity.Property(message => message.MenuPath)
            .HasColumnName("menu_path")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        entity.HasIndex(message => new { message.TicketId, message.CreatedAt })
            .HasDatabaseName("ix_ticket_message_ticket_created");
    }
}
