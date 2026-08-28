return @{
    Services = @(
        @{
            Name = 'GreyGray-Storefront'
            Kind = 'DotNet'
            ArtifactDirectory = 'GreyGray.Api.Storefront'
            ArtifactEntryPoint = 'GreyGray.Api.Storefront.exe'
            Executable = 'GreyGray.Api.Storefront.exe'
            Arguments = @()
            WorkingDirectory = '.'
            ProcessName = 'GreyGray.Api.Storefront'
            Port = 5000
            HealthPath = '/health'
        }
        @{
            Name = 'GreyGray-Admin'
            Kind = 'DotNet'
            ArtifactDirectory = 'GreyGray.Api.Admin'
            ArtifactEntryPoint = 'GreyGray.Api.Admin.exe'
            Executable = 'GreyGray.Api.Admin.exe'
            Arguments = @()
            WorkingDirectory = '.'
            ProcessName = 'GreyGray.Api.Admin'
            Port = 5001
            HealthPath = '/health'
        }
        @{
            Name = 'GreyGray-Worker'
            Kind = 'DotNet'
            ArtifactDirectory = 'GreyGray.Worker'
            ArtifactEntryPoint = 'GreyGray.Worker.exe'
            Executable = 'GreyGray.Worker.exe'
            Arguments = @()
            WorkingDirectory = '.'
            ProcessName = 'GreyGray.Worker'
            Port = 0
            HealthPath = $null
        }
        @{
            Name = 'GreyGray-Web-Storefront'
            Kind = 'NextStandalone'
            ArtifactDirectory = 'GreyGray.Web.Storefront'
            ArtifactEntryPoint = 'apps\storefront\server.js'
            Executable = 'node.exe'
            Arguments = @('server.js')
            WorkingDirectory = 'apps\storefront'
            ProcessName = 'node'
            Port = 5002
            HealthPath = '/'
        }
        @{
            Name = 'GreyGray-Web-Admin'
            Kind = 'NextStandalone'
            ArtifactDirectory = 'GreyGray.Web.Admin'
            ArtifactEntryPoint = 'apps\admin\server.js'
            Executable = 'node.exe'
            Arguments = @('server.js')
            WorkingDirectory = 'apps\admin'
            ProcessName = 'node'
            Port = 5003
            HealthPath = '/'
        }
    )
}
