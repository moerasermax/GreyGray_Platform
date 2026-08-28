using GreyGray.Modules.Catalog.Infra;
using GreyGray.Modules.Identity.Infra;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

public sealed class ArchitectureSurfaceTests
{
    [Fact]
    public void Infra_exports_only_the_two_module_registration_roots()
    {
        typeof(IdentityModuleRegistration).Assembly.ExportedTypes
            .Select(type => type.FullName)
            .ShouldBe([typeof(IdentityModuleRegistration).FullName]);
        typeof(CatalogModuleRegistration).Assembly.ExportedTypes
            .Select(type => type.FullName)
            .ShouldBe([typeof(CatalogModuleRegistration).FullName]);
    }
}
