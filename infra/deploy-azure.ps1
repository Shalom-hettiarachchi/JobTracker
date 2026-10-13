<#
.SYNOPSIS
    Creates the Azure resources for JobTracker on free-tier offers and deploys the container.

.DESCRIPTION
    - Azure SQL Database, serverless, using the free offer (auto-pauses when the monthly
      free allowance runs out, so it cannot generate a bill).
    - Azure Container Apps on the consumption plan, scaling to zero when idle (covered by
      the monthly free grant).

    Run `az login` first. The script is safe to run again; it updates what already exists.

.EXAMPLE
    ./infra/deploy-azure.ps1 -AppName jobtracker-shalom -Location southeastasia
#>
[CmdletBinding()]
param(
    # Globally unique: used for the SQL server name and the app's public URL.
    [Parameter(Mandatory)] [string] $AppName,
    [string] $ResourceGroup = "$AppName-rg",
    [string] $Location = 'southeastasia',
    [string] $Image = 'ghcr.io/shalom-hettiarachchi/jobtracker:latest',
    # IANA time zone the app shows times in, e.g. Asia/Colombo.
    [string] $TimeZone = 'UTC',
    [string] $SqlAdminUser = 'jobtracker_admin',
    [Parameter(Mandatory)] [securestring] $SqlAdminPassword,
    # Only needed while the container image on ghcr.io is private.
    [string] $RegistryUser,
    [securestring] $RegistryToken
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args[0..1] -join ' ') failed." }
}

$sqlPassword = [System.Net.NetworkCredential]::new('', $SqlAdminPassword).Password
$sqlServer = "$AppName-sql"
$database = 'JobTracker'
$environment = "$AppName-env"

Write-Host "Resource group $ResourceGroup ($Location)..."
Invoke-Az group create --name $ResourceGroup --location $Location --output none

Write-Host "SQL server $sqlServer..."
Invoke-Az sql server create --name $sqlServer --resource-group $ResourceGroup --location $Location `
    --admin-user $SqlAdminUser --admin-password $sqlPassword --output none

# 0.0.0.0 is Azure's "allow Azure services" rule: the container app can connect, the internet cannot.
Invoke-Az sql server firewall-rule create --server $sqlServer --resource-group $ResourceGroup `
    --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none

Write-Host "Database $database (free offer, serverless)..."
Invoke-Az sql db create --server $sqlServer --resource-group $ResourceGroup --name $database `
    --edition GeneralPurpose --family Gen5 --capacity 2 --compute-model Serverless `
    --use-free-limit --free-limit-exhaustion-behavior AutoPause --output none

$connectionString = "Server=tcp:$sqlServer.database.windows.net,1433;Database=$database;" +
    "User ID=$SqlAdminUser;Password=$sqlPassword;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60"

Write-Host "Container Apps environment $environment..."
Invoke-Az extension add --name containerapp --upgrade --only-show-errors
Invoke-Az containerapp env create --name $environment --resource-group $ResourceGroup --location $Location `
    --logs-destination none --output none

Write-Host "Container app $AppName..."
$create = @(
    'containerapp', 'create', '--name', $AppName, '--resource-group', $ResourceGroup,
    '--environment', $environment, '--image', $Image,
    '--ingress', 'external', '--target-port', '8080',
    # One replica at most: Blazor Server keeps each visitor's session in memory.
    '--min-replicas', '0', '--max-replicas', '1',
    '--cpu', '0.25', '--memory', '0.5Gi',
    '--secrets', "db-connection=$connectionString",
    '--env-vars', 'ConnectionStrings__DefaultConnection=secretref:db-connection', "TZ=$TimeZone",
    '--output', 'none'
)
if ($RegistryUser -and $RegistryToken) {
    $create += '--registry-server', 'ghcr.io', '--registry-username', $RegistryUser,
        '--registry-password', [System.Net.NetworkCredential]::new('', $RegistryToken).Password
}
Invoke-Az @create

$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup `
    --query properties.configuration.ingress.fqdn --output tsv

Write-Host ''
Write-Host "Deployed: https://$fqdn"
Write-Host 'The first request after a quiet period takes up to a minute while the app and database wake up.'
Write-Host ''
Write-Host 'To let GitHub Actions deploy on every push, set these in the repository settings:'
Write-Host "  Variable AZURE_CONTAINER_APP  = $AppName"
Write-Host "  Variable AZURE_RESOURCE_GROUP = $ResourceGroup"
Write-Host '  Secret   AZURE_CREDENTIALS    = output of:'
$scope = az group show --name $ResourceGroup --query id --output tsv
Write-Host "    az ad sp create-for-rbac --name $AppName-deploy --role contributor --scopes $scope --json-auth"
