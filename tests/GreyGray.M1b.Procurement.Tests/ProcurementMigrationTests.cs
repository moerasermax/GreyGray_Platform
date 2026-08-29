using Shouldly;
using Xunit;

namespace GreyGray.M1b.Procurement.Tests;

public sealed class ProcurementMigrationTests
{
    [Fact(DisplayName = "0007 以 owner 建表、鎖 tenant/order line 唯一且不跨 schema FK")]
    public void Migration_preserves_owner_tenant_and_schema_boundaries()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "db",
            "migrations",
            "0007_m1b_procurement.sql"));

        sql.ShouldContain("SET ROLE greygray_owner;");
        sql.ShouldContain("CREATE TABLE IF NOT EXISTS procurement.purchase_item");
        sql.ShouldContain("ux_purchase_item_tenant_order_line");
        sql.ShouldContain("(tenant_id, order_line_id)");
        sql.ShouldContain("actual_paid_booking_currency = 'TWD'");
        sql.ShouldNotContain("REFERENCES ordering.");
        sql.ShouldNotContain("REFERENCES campaign.");
        sql.ShouldNotContain("REFERENCES catalog.");
    }

    [Fact(DisplayName = "0011 以 owner 建表、tenant composite FK 鎖住 inquiry 且不跨 schema FK")]
    public void Migration_0011_preserves_owner_tenant_and_schema_boundaries()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "db",
            "migrations",
            "0011_m1b_compensation.sql"));

        sql.ShouldContain("SET ROLE greygray_owner;");
        sql.ShouldContain("CREATE TABLE IF NOT EXISTS procurement.inquiry");
        sql.ShouldContain("purchase_item_tenant_id_unique UNIQUE (tenant_id, id)");
        sql.ShouldContain("REFERENCES procurement.purchase_item (tenant_id, id)");
        sql.ShouldContain("ux_inquiry_purchase_item_open");
        sql.ShouldNotContain("REFERENCES ordering.");
        sql.ShouldNotContain("REFERENCES campaign.");
        sql.ShouldNotContain("REFERENCES catalog.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GreyGray.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到 GreyGray.slnx，無法定位 migration。");
    }
}
