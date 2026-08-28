return @{
    Services = @(
        @{
            Name = 'GreyGray-Storefront'
            ArtifactDirectory = 'GreyGray.Api.Storefront'
            Executable = 'GreyGray.Api.Storefront.exe'
            Port = 5000
            HealthPath = '/health'
        }
        @{
            Name = 'GreyGray-Admin'
            ArtifactDirectory = 'GreyGray.Api.Admin'
            Executable = 'GreyGray.Api.Admin.exe'
            Port = 5001
            HealthPath = '/health'
        }
        @{
            Name = 'GreyGray-Worker'
            ArtifactDirectory = 'GreyGray.Worker'
            Executable = 'GreyGray.Worker.exe'
            Port = 0
            HealthPath = $null
        }
    )
}
